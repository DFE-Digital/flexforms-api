using GovUK.Dfe.FlexForms.Utils.Caching;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Utils.Configuration;

/// <summary>
/// Resolves the <see cref="IConfiguration"/> used when registering CoreLibs host services
/// (notifications, file storage, caching, email) at startup.
/// </summary>
public static class CoreLibsHostConfiguration
{
    public const string GlobalConfigurationSection = "GlobalConfiguration";
    public const string FileStorageProviderKey = "FileStorage:Provider";
    public const string EmailProviderKey = "Email:Provider";

    /// <summary>
    /// Builds host-shaped configuration for CoreLibs DI.
    /// Prefers <c>GlobalConfiguration</c> when it contains required host sections.
    /// Falls back to the first tenant only in Local / Development / CodeGeneration (or when
    /// <c>AllowTenantHostConfigFallback=true</c>) so local, NSwag, and integration tests keep working.
    /// Non-local environments require <c>GlobalConfiguration:FileStorage:Provider</c>
    /// so a misconfigured tenant cannot take down the API process.
    /// </summary>
    public static IConfiguration Resolve(IConfiguration root, IConfiguration? firstTenantSettings)
    {
        ArgumentNullException.ThrowIfNull(root);

        var global = root.GetSection(GlobalConfigurationSection);
        var globalHasFileStorage = HasNonEmptyValue(global, FileStorageProviderKey);

        if (globalHasFileStorage)
        {
            // FileStorage comes from GlobalConfiguration; Redis/Email gaps may still be
            // filled from root or the first tenant so CodeGeneration / Local keep working.
            return OverlayFlexFormsCacheAndRedis(global, root, firstTenantSettings);
        }

        if (AllowTenantFallback(root)
            && firstTenantSettings is not null
            && HasNonEmptyValue(firstTenantSettings, FileStorageProviderKey))
        {
            return OverlayFlexFormsCacheAndRedis(firstTenantSettings, root, fallback: null);
        }

        throw new InvalidOperationException(
            "GlobalConfiguration:FileStorage:Provider is required for host FileStorage registration. "
            + "Set GlobalConfiguration__FileStorage__Provider (and Azure/Local settings) on the API host. "
            + "Do not rely on TenantConfig FileStorage for process startup — that is optional per-tenant "
            + "runtime overlay only. "
            + "Local/Development/CodeGeneration may fall back to the first tenant when "
            + "AllowTenantHostConfigFallback is not disabled.");
    }

    /// <summary>
    /// True when Local/Development/CodeGeneration (or explicit flag) may use first-tenant settings for host DI.
    /// </summary>
    public static bool AllowTenantFallback(IConfiguration root)
    {
        if (root.GetValue<bool>("AllowTenantHostConfigFallback"))
            return true;

        // Explicit opt-out for Local boxes that should mirror Test/Prod.
        if (root.GetValue<bool?>("AllowTenantHostConfigFallback") == false)
            return false;

        var env = root["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? string.Empty;

        return env.Equals("Local", StringComparison.OrdinalIgnoreCase)
            || env.Equals("Development", StringComparison.OrdinalIgnoreCase)
            || env.Equals("CodeGeneration", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNonEmptyValue(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key]);

    private static IConfiguration OverlayFlexFormsCacheAndRedis(
        IConfiguration primary,
        IConfiguration root,
        IConfiguration? fallback)
    {
        var overlay = new Dictionary<string, string?>
        {
            // Always win over legacy EAT / tenant values of DfE:Cache:
            ["CacheSettings:Redis:KeyPrefix"] = FlexFormsCacheKeys.RedisKeyPrefix,
            ["NotificationService:RedisKeyPrefix"] = FlexFormsCacheKeys.NotificationsKeyPrefix,
        };

        if (!HasRedisConnection(primary))
        {
            var redis = ResolveRedisConnection(root) ?? ResolveRedisConnection(fallback);
            if (!string.IsNullOrWhiteSpace(redis))
            {
                overlay["ConnectionStrings:Redis"] = redis;
                overlay["NotificationService:RedisConnectionString"] = redis;
            }
        }

        // Host appsettings CacheSettings (except KeyPrefix already forced above).
        // Host Durations win over legacy tenant/EAT values (e.g. 1s listing TTLs).
        var hostRedisDuration = root["CacheSettings:Redis:DefaultDurationInSeconds"];
        if (!string.IsNullOrWhiteSpace(hostRedisDuration))
            overlay["CacheSettings:Redis:DefaultDurationInSeconds"] = hostRedisDuration;

        var hostDb = root["CacheSettings:Redis:Database"];
        if (!string.IsNullOrWhiteSpace(hostDb))
            overlay["CacheSettings:Redis:Database"] = hostDb;

        foreach (var duration in root.GetSection("CacheSettings:Redis:Durations").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(duration.Key) && !string.IsNullOrWhiteSpace(duration.Value))
                overlay[$"CacheSettings:Redis:Durations:{duration.Key}"] = duration.Value;
        }

        return new ConfigurationBuilder()
            .AddConfiguration(primary)
            .AddInMemoryCollection(overlay)
            .Build();
    }

    private static string? ResolveRedisConnection(IConfiguration? configuration)
    {
        if (configuration is null)
            return null;

        return configuration.GetConnectionString("Redis")
            ?? configuration["ConnectionStrings:Redis"]
            ?? configuration["Redis:ConnectionString"]
            ?? configuration["NotificationService:RedisConnectionString"]
            ?? configuration["Redis"];
    }

    private static bool HasRedisConnection(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(ResolveRedisConnection(configuration));
}
