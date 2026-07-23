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
    /// otherwise uses the first tenant's settings. Overlays Redis from root config when the
    /// primary source does not define a Redis connection string (tests and shared host Redis).
    /// </summary>
    public static IConfiguration Resolve(IConfiguration root, IConfiguration firstTenantSettings)
    {
        var global = root.GetSection("GlobalConfiguration");
        var primary = global.Exists() && global.GetSection("FileStorage").GetChildren().Any()
            ? (IConfiguration)global
            : firstTenantSettings;

        return OverlayRedisFromRoot(primary, root);
    }

    private static IConfiguration OverlayRedisFromRoot(IConfiguration primary, IConfiguration root)
    {
        if (HasRedisConnection(primary))
            return primary;

        var redis = root.GetConnectionString("Redis")
            ?? root["Redis:ConnectionString"]
            ?? root["NotificationService:RedisConnectionString"];

        if (string.IsNullOrWhiteSpace(redis))
            return primary;

        return new ConfigurationBuilder()
            .AddConfiguration(primary)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = redis,
                ["NotificationService:RedisConnectionString"] = redis,
            })
            .Build();
    }

    private static bool HasRedisConnection(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis"))
        || !string.IsNullOrWhiteSpace(configuration["Redis:ConnectionString"])
        || !string.IsNullOrWhiteSpace(configuration["NotificationService:RedisConnectionString"])
        || !string.IsNullOrWhiteSpace(configuration["Redis"]);
}
