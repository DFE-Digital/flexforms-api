using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Loads tenant configuration from the tenant config database and caches it in memory.
/// Implements IHostedService to load at startup and refresh periodically.
/// Uses volatile dictionary swaps for lock-free reads.
/// <para>
/// The snapshot is only swapped, and <see cref="ITenantConfigurationChangedNotifier"/> only fired,
/// when the loaded data differs from the current snapshot. Subscribers rebuild expensive state
/// (OIDC metadata caches, auth registries, options) on every notification, so an unconditional
/// notify on each periodic refresh would discard those caches every interval.
/// </para>
/// </summary>
public class DatabaseTenantConfigurationProvider(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseTenantConfigurationProvider> logger,
    ITenantSettingsEncryptor? encryptor = null,
    string targetApplication = "Api",
    int refreshIntervalSeconds = 60,
    ITenantConfigurationChangedNotifier? changeNotifier = null) : ITenantConfigurationProvider, ITenantConfigurationRefreshState, IHostedService, IDisposable
{
    private volatile IReadOnlyDictionary<Guid, TenantConfiguration> _tenantsById =
        new Dictionary<Guid, TenantConfiguration>();

    private volatile IReadOnlyDictionary<string, TenantConfiguration> _tenantsByOrigin =
        new Dictionary<string, TenantConfiguration>(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset? _lastRefreshedUtc;

    private string? _snapshotFingerprint;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private Timer? _refreshTimer;
    private bool _disposed;

    public string Source => "Database";

    public DateTimeOffset? LastRefreshedUtc => _lastRefreshedUtc;

    public int ActiveTenantCount => _tenantsById.Count;

    /// <inheritdoc />
    public TenantConfiguration? GetTenant(Guid id)
        => _tenantsById.TryGetValue(id, out var tenant) ? tenant : null;

    /// <inheritdoc />
    public TenantConfiguration? GetTenantByOrigin(string origin)
        => _tenantsByOrigin.TryGetValue(origin, out var tenant) ? tenant : null;

    /// <inheritdoc />
    public IReadOnlyCollection<TenantConfiguration> GetAllTenants()
        => _tenantsById.Values.ToArray();

    /// <summary>
    /// Triggers an immediate refresh of the tenant configuration cache.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Serialised so the fingerprint comparison and snapshot swap cannot interleave between
        // the timer and admin-triggered refreshes.
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TenantConfigDbContext>();

            var tenantEntities = await dbContext.Tenants
                .AsNoTracking()
                .Where(t => t.IsActive)
                .Include(t => t.Settings.Where(s => s.Target == "Shared" || s.Target == targetApplication))
                .Include(t => t.Hostnames)
                .Include(t => t.FrontendOrigins)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

            var newTenantsById = new Dictionary<Guid, TenantConfiguration>();
            var newTenantsByOrigin = new Dictionary<string, TenantConfiguration>(StringComparer.OrdinalIgnoreCase);
            using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // Deterministic order: the query has no ORDER BY, and both the fingerprint and the
            // last-wins origin index must not depend on whatever order the database returns.
            foreach (var entity in tenantEntities.OrderBy(t => t.Id))
            {
                var configPairs = new List<KeyValuePair<string, string?>>();

                foreach (var setting in entity.Settings)
                {
                    string json;
                    if (setting.IsSecret && encryptor != null)
                    {
                        try
                        {
                            json = encryptor.Decrypt(setting.Settings);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(
                                ex,
                                "Failed to decrypt secret setting '{Category}' (Target={Target}) for tenant '{TenantName}' ({TenantId}). Skipping setting.",
                                setting.Category,
                                setting.Target,
                                entity.Name,
                                entity.Id);
                            continue;
                        }
                    }
                    else
                    {
                        json = setting.Settings;
                    }

                    FlattenJson(setting.Category, json, configPairs);
                }

                var settings = new ConfigurationBuilder()
                    .AddInMemoryCollection(configPairs)
                    .Build();

                var origins = entity.FrontendOrigins
                    .Select(o => o.Origin)
                    .ToArray();

                var tenantConfig = new TenantConfiguration(entity.Id, entity.Name, settings, origins);

                newTenantsById[entity.Id] = tenantConfig;

                foreach (var origin in origins)
                {
                    newTenantsByOrigin[origin] = tenantConfig;
                }

                AppendTenantToFingerprint(fingerprint, entity.Id, entity.Name, configPairs, origins);
            }

            var newFingerprint = Convert.ToHexString(fingerprint.GetHashAndReset());
            _lastRefreshedUtc = DateTimeOffset.UtcNow;

            if (string.Equals(newFingerprint, _snapshotFingerprint, StringComparison.Ordinal))
            {
                logger.LogDebug(
                    "Tenant configuration unchanged ({TenantCount} active tenants for target '{Target}'); keeping current snapshot",
                    newTenantsById.Count, targetApplication);
                return;
            }

            _tenantsById = newTenantsById;
            _tenantsByOrigin = newTenantsByOrigin;
            _snapshotFingerprint = newFingerprint;

            logger.LogInformation(
                "Tenant configuration refreshed. Loaded {TenantCount} active tenants for target '{Target}'",
                newTenantsById.Count, targetApplication);

            // SaaS hot-reload: notify in-process subscribers (e.g. ITenantAuthProviderRegistry) that
            // the snapshot has been swapped. Must run AFTER the volatile assignments above so
            // subscribers observing GetAllTenants() see the new data.
            changeNotifier?.Notify();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to refresh tenant configuration from database. Retaining previous configuration");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Feeds everything that ends up in a <see cref="TenantConfiguration"/> into the snapshot
    /// fingerprint. Values are length-prefixed so adjacent fields cannot run into each other,
    /// and null is distinguished from empty string.
    /// </summary>
    private static void AppendTenantToFingerprint(
        IncrementalHash hash,
        Guid tenantId,
        string tenantName,
        List<KeyValuePair<string, string?>> configPairs,
        string[] origins)
    {
        AppendFingerprintValue(hash, tenantId.ToString("N"));
        AppendFingerprintValue(hash, tenantName);

        // Stable sort: duplicate keys keep their relative order, which decides last-wins.
        AppendFingerprintValue(hash, configPairs.Count.ToString());
        foreach (var pair in configPairs.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            AppendFingerprintValue(hash, pair.Key);
            AppendFingerprintValue(hash, pair.Value);
        }

        AppendFingerprintValue(hash, origins.Length.ToString());
        foreach (var origin in origins.Order(StringComparer.OrdinalIgnoreCase))
        {
            AppendFingerprintValue(hash, origin);
        }
    }

    private static void AppendFingerprintValue(IncrementalHash hash, string? value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        if (value is null)
        {
            BitConverter.TryWriteBytes(length, -1);
            hash.AppendData(length);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        BitConverter.TryWriteBytes(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Loading tenant configuration from database for target '{Target}'...", targetApplication);

        await RefreshAsync(cancellationToken);

        if (!_tenantsById.Any())
        {
            logger.LogWarning("No active tenants loaded from the tenant configuration database");
        }

        _refreshTimer = new Timer(
            _ => _ = RefreshAsync(CancellationToken.None),
            null,
            TimeSpan.FromSeconds(refreshIntervalSeconds),
            TimeSpan.FromSeconds(refreshIntervalSeconds));
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _refreshTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Flattens a JSON blob into key-value pairs prefixed by category.
    /// E.g. category "DfESignIn" with JSON {"ClientId":"abc"} becomes "DfESignIn:ClientId" = "abc".
    /// </summary>
    private static void FlattenJson(
        string category,
        string json,
        List<KeyValuePair<string, string?>> result)
    {
        using var doc = JsonDocument.Parse(json);
        FlattenJsonElement(category, doc.RootElement, result);
    }

    private static void FlattenJsonElement(
        string prefix,
        JsonElement element,
        List<KeyValuePair<string, string?>> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var wroteChild = false;
                foreach (var property in element.EnumerateObject())
                {
                    wroteChild = true;
                    FlattenJsonElement($"{prefix}:{property.Name}", property.Value, result);
                }

                // Empty objects (e.g. {"HostMappings":{}}) must still emit a key so
                // consumers can detect an explicit empty section after flatten.
                if (!wroteChild)
                {
                    result.Add(new KeyValuePair<string, string?>(prefix, null));
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJsonElement($"{prefix}:{index}", item, result);
                    index++;
                }
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                // Preserve JSON null (do not coerce to ""). Empty strings break round-trip
                // deserialize of EventMappings (e.g. sourceFieldIds: List<string>).
                result.Add(new KeyValuePair<string, string?>(prefix, null));
                break;

            case JsonValueKind.String:
                result.Add(new KeyValuePair<string, string?>(prefix, element.GetString()));
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result.Add(new KeyValuePair<string, string?>(prefix, element.ToString()));
                break;

            default:
                result.Add(new KeyValuePair<string, string?>(prefix, element.ToString()));
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Dispose();
    }
}
