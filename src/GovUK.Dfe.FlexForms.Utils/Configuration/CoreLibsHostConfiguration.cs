using GovUK.Dfe.FlexForms.Utils.Caching;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Utils.Configuration;

/// <summary>
/// Resolves the <see cref="IConfiguration"/> used when registering CoreLibs host services
/// (notifications, file storage, caching, email) at startup.
/// </summary>
public static class CoreLibsHostConfiguration
{
    /// <summary>
    /// Prefers <c>GlobalConfiguration</c> when it already contains FileStorage (host shape);
    /// otherwise uses the first tenant's settings. Overlays Redis connection from root when needed,
    /// and always forces FlexForms Redis key prefixes so shared Redis with legacy EAT does not collide.
    /// </summary>
    public static IConfiguration Resolve(IConfiguration root, IConfiguration firstTenantSettings)
    {
        var global = root.GetSection("GlobalConfiguration");
        var primary = global.Exists() && global.GetSection("FileStorage").GetChildren().Any()
            ? (IConfiguration)global
            : firstTenantSettings;

        return OverlayFlexFormsCacheAndRedis(primary, root);
    }

    private static IConfiguration OverlayFlexFormsCacheAndRedis(IConfiguration primary, IConfiguration root)
    {
        var overlay = new Dictionary<string, string?>
        {
            // Always win over legacy EAT / tenant values of DfE:Cache:
            ["CacheSettings:Redis:KeyPrefix"] = FlexFormsCacheKeys.RedisKeyPrefix,
            ["NotificationService:RedisKeyPrefix"] = FlexFormsCacheKeys.NotificationsKeyPrefix,
        };

        if (!HasRedisConnection(primary))
        {
            var redis = root.GetConnectionString("Redis")
                ?? root["Redis:ConnectionString"]
                ?? root["NotificationService:RedisConnectionString"];

            if (!string.IsNullOrWhiteSpace(redis))
            {
                overlay["ConnectionStrings:Redis"] = redis;
                overlay["NotificationService:RedisConnectionString"] = redis;
            }
        }

        // Host appsettings CacheSettings (except KeyPrefix already forced above)
        var hostRedisDuration = root["CacheSettings:Redis:DefaultDurationInSeconds"];
        if (!string.IsNullOrWhiteSpace(hostRedisDuration))
            overlay["CacheSettings:Redis:DefaultDurationInSeconds"] = hostRedisDuration;

        var hostDb = root["CacheSettings:Redis:Database"];
        if (!string.IsNullOrWhiteSpace(hostDb))
            overlay["CacheSettings:Redis:Database"] = hostDb;

        return new ConfigurationBuilder()
            .AddConfiguration(primary)
            .AddInMemoryCollection(overlay)
            .Build();
    }

    private static bool HasRedisConnection(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis"))
        || !string.IsNullOrWhiteSpace(configuration["Redis:ConnectionString"])
        || !string.IsNullOrWhiteSpace(configuration["NotificationService:RedisConnectionString"])
        || !string.IsNullOrWhiteSpace(configuration["Redis"]);
}
