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
                .ToListAsync(cancellationToken);

            var newTenantsById = new Dictionary<Guid, TenantConfiguration>();
            var newTenantsByOrigin = new Dictionary<string, TenantConfiguration>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in tenantEntities)
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
            }

            _tenantsById = newTenantsById;
            _tenantsByOrigin = newTenantsByOrigin;
            _lastRefreshedUtc = DateTimeOffset.UtcNow;

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
                foreach (var property in element.EnumerateObject())
                {
                    FlattenJsonElement($"{prefix}:{property.Name}", property.Value, result);
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
