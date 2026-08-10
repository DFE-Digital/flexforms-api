using System.Text.Json;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Database;

/// <summary>
/// Seeds the tenant config database from the existing appsettings.json Tenants section.
/// Run once to migrate from file-based to database-backed tenant configuration.
/// </summary>
public static class TenantConfigSeeder
{
    private static readonly HashSet<string> SecretCategories = TenantSettingsSecretCategories.All
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SkipKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "Name", "Web", "Hostnames"
    };

    /// <summary>
    /// Seeds tenants from appsettings into the database if the Tenants table is empty.
    /// </summary>
    public static async Task SeedFromAppSettingsAsync(
        TenantConfigDbContext dbContext,
        IConfiguration configuration,
        ITenantSettingsEncryptor? encryptor = null,
        ILogger? logger = null,
        string defaultTarget = "Api")
    {
        if (dbContext.Tenants.Any())
        {
            logger?.LogInformation("Tenant config database already contains data. Skipping seed");
            return;
        }

        var tenantsSection = configuration.GetSection("Tenants");
        if (!tenantsSection.Exists())
        {
            logger?.LogWarning("No 'Tenants' section found in configuration. Nothing to seed");
            return;
        }

        var usedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tenantSection in tenantsSection.GetChildren())
        {
            var tenantIdStr = tenantSection["Id"];
            var tenantName = tenantSection["Name"] ?? tenantSection.Key;

            if (string.IsNullOrWhiteSpace(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            {
                logger?.LogWarning("Skipping tenant '{TenantKey}': missing or invalid Id", tenantSection.Key);
                continue;
            }

            var tenantEntity = new TenantEntity
            {
                Id = tenantId,
                Name = tenantName,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            // Extract frontend origins, skipping duplicates across tenants
            var originsSection = tenantSection.GetSection("Frontend:Origins");
            var originsList = originsSection.Exists()
                ? originsSection.Get<string[]>() ?? []
                : new[] { tenantSection["Frontend:Origin"] ?? "" };

            foreach (var origin in originsList.Where(o => !string.IsNullOrWhiteSpace(o)))
            {
                if (!usedOrigins.Add(origin))
                {
                    logger?.LogWarning(
                        "Skipping duplicate origin '{Origin}' for tenant '{TenantName}' -- already assigned to another tenant",
                        origin, tenantName);
                    continue;
                }

                tenantEntity.FrontendOrigins.Add(new TenantFrontendOriginEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Origin = origin
                });
            }

            var hostnamesSection = tenantSection.GetSection("Hostnames");
            var hostnames = hostnamesSection.Exists()
                ? hostnamesSection.Get<string[]>() ?? []
                : [];

            foreach (var hostname in hostnames.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                tenantEntity.Hostnames.Add(new TenantHostnameEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Hostname = hostname.Trim()
                });
            }

            // Group child sections into categories and serialize each as JSON
            foreach (var categorySection in tenantSection.GetChildren())
            {
                if (SkipKeys.Contains(categorySection.Key))
                    continue;

                var categoryJson = SerializeSectionToJson(categorySection);
                if (string.IsNullOrWhiteSpace(categoryJson) || categoryJson == "{}")
                    continue;

                var isSecret = SecretCategories.Contains(categorySection.Key);
                var storedJson = isSecret && encryptor != null
                    ? encryptor.Encrypt(categoryJson)
                    : categoryJson;

                tenantEntity.Settings.Add(new TenantSettingEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Category = categorySection.Key,
                    Target = defaultTarget,
                    Settings = storedJson,
                    IsSecret = isSecret,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            var webSection = tenantSection.GetSection("Web");
            if (webSection.Exists())
            {
                foreach (var webCategorySection in webSection.GetChildren())
                {
                    var webCategoryJson = SerializeSectionToJson(webCategorySection);
                    if (string.IsNullOrWhiteSpace(webCategoryJson) || webCategoryJson == "{}")
                    {
                        continue;
                    }

                    var webIsSecret = SecretCategories.Contains(webCategorySection.Key);
                    var webStoredJson = webIsSecret && encryptor != null
                        ? encryptor.Encrypt(webCategoryJson)
                        : webCategoryJson;

                    tenantEntity.Settings.Add(new TenantSettingEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        Category = webCategorySection.Key,
                        Target = "Web",
                        Settings = webStoredJson,
                        IsSecret = webIsSecret,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            dbContext.Tenants.Add(tenantEntity);
            logger?.LogInformation(
                "Seeded tenant '{TenantName}' ({TenantId}) with {SettingsCount} setting categories, {OriginsCount} origins and {HostnamesCount} hostnames",
                tenantName, tenantId, tenantEntity.Settings.Count, tenantEntity.FrontendOrigins.Count, tenantEntity.Hostnames.Count);
        }

        await dbContext.SaveChangesAsync();
        logger?.LogInformation("Tenant config seeding complete");
    }

    private static string SerializeSectionToJson(IConfigurationSection section)
    {
        var result = BuildValue(section);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Recursively converts an IConfigurationSection into a JSON-compatible object tree:
    /// scalars become strings, array sections become List, object sections become Dictionary.
    /// </summary>
    private static object? BuildValue(IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();

        if (!children.Any())
            return section.Value;

        if (children.All(c => int.TryParse(c.Key, out _)))
        {
            return children
                .OrderBy(c => int.Parse(c.Key))
                .Select(BuildValue)
                .ToList();
        }

        var dict = new Dictionary<string, object?>();
        foreach (var child in children)
        {
            dict[child.Key] = BuildValue(child);
        }
        return dict;
    }
}
