using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Clones TenantConfig rows for a quick new-tenant bootstrap.
/// </summary>
public sealed class TenantDuplicatorService(
    TenantConfigDbContext dbContext,
    ITenantSettingsEncryptor encryptor,
    ILogger<TenantDuplicatorService> logger) : ITenantDuplicator
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = false
    };

    public async Task<DuplicateTenantResult> DuplicateAsync(
        Guid sourceTenantId,
        Guid newTenantId,
        string newTenantName,
        string hostname,
        string frontendOrigin,
        string authorizationApiSecretKey,
        string internalServiceAuthSecretKey,
        IReadOnlyList<(string Email, string ApiKey)> internalServiceAuthServiceApiKeys,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        if (newTenantId == Guid.Empty)
            throw new InvalidOperationException("New tenant id is required.");

        newTenantName = (newTenantName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newTenantName))
            throw new InvalidOperationException("New tenant name is required.");
        if (newTenantName.Length > 100)
            throw new InvalidOperationException("New tenant name must not exceed 100 characters.");

        serviceName = (serviceName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new InvalidOperationException("Service name is required.");
        if (serviceName.Length > 200)
            throw new InvalidOperationException("Service name must not exceed 200 characters.");

        hostname = NormalizeHostname(hostname);
        frontendOrigin = NormalizeOrigin(frontendOrigin);

        if (string.IsNullOrWhiteSpace(hostname))
            throw new InvalidOperationException("Hostname is required.");
        if (hostname.Length > 255)
            throw new InvalidOperationException("Hostname must not exceed 255 characters.");

        if (string.IsNullOrWhiteSpace(frontendOrigin))
            throw new InvalidOperationException("Frontend origin is required.");
        if (frontendOrigin.Length > 500)
            throw new InvalidOperationException("Frontend origin must not exceed 500 characters.");

        authorizationApiSecretKey = (authorizationApiSecretKey ?? string.Empty).Trim();
        internalServiceAuthSecretKey = (internalServiceAuthSecretKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(authorizationApiSecretKey))
            throw new InvalidOperationException("Authorization API secret key is required.");
        if (authorizationApiSecretKey.Length < 32)
            throw new InvalidOperationException("Authorization API secret key must be at least 32 characters.");
        if (string.IsNullOrWhiteSpace(internalServiceAuthSecretKey))
            throw new InvalidOperationException("InternalServiceAuth secret key is required.");
        if (internalServiceAuthSecretKey.Length < 32)
            throw new InvalidOperationException("InternalServiceAuth secret key must be at least 32 characters.");

        var serviceApiKeys = NormalizeServiceApiKeys(internalServiceAuthServiceApiKeys);

        if (newTenantId == sourceTenantId)
            throw new InvalidOperationException("New tenant id must differ from the source tenant id.");

        var source = await dbContext.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == sourceTenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Source tenant '{sourceTenantId}' was not found.");

        if (await dbContext.Tenants.AnyAsync(t => t.Id == newTenantId, cancellationToken))
            throw new InvalidOperationException($"Tenant id '{newTenantId}' already exists.");

        if (await dbContext.Tenants.AnyAsync(
                t => t.Name == newTenantName, cancellationToken))
            throw new InvalidOperationException($"Tenant name '{newTenantName}' is already in use.");

        if (await dbContext.TenantHostnames.AnyAsync(
                h => h.Hostname == hostname, cancellationToken))
            throw new InvalidOperationException($"Hostname '{hostname}' is already assigned to another tenant.");

        if (await dbContext.TenantFrontendOrigins.AnyAsync(
                o => o.Origin == frontendOrigin, cancellationToken))
            throw new InvalidOperationException($"Frontend origin '{frontendOrigin}' is already assigned to another tenant.");

        var now = DateTime.UtcNow;
        var newTenant = new TenantEntity
        {
            Id = newTenantId,
            Name = newTenantName,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        newTenant.Hostnames.Add(new TenantHostnameEntity
        {
            Id = Guid.NewGuid(),
            TenantId = newTenantId,
            Hostname = hostname
        });

        newTenant.FrontendOrigins.Add(new TenantFrontendOriginEntity
        {
            Id = Guid.NewGuid(),
            TenantId = newTenantId,
            Origin = frontendOrigin
        });

        string? internalAuthTemplate = null;
        var internalAuthTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var internalAuthIsSecret = true;

        foreach (var setting in source.Settings)
        {
            var plaintext = setting.IsSecret
                ? encryptor.Decrypt(setting.Settings)
                : setting.Settings;

            if (string.Equals(setting.Category, "InternalServiceAuth", StringComparison.OrdinalIgnoreCase))
            {
                internalAuthTargets.Add(setting.Target);
                internalAuthIsSecret = setting.IsSecret;

                // Prefer Api as the template so Web gets an exact copy of the Api payload.
                if (internalAuthTemplate is null ||
                    string.Equals(setting.Target, "Api", StringComparison.OrdinalIgnoreCase))
                {
                    internalAuthTemplate = plaintext;
                }

                continue;
            }

            // Do not copy form-template bindings — the new tenant starts with no templates
            // and must create its own (avoids inheriting source HostMappings / Template:Id).
            if (IsTemplateBindingCategory(setting.Category))
            {
                continue;
            }

            if (string.Equals(setting.Category, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                plaintext = ApplyAuthorizationApiSecretKey(plaintext, authorizationApiSecretKey, newTenantId);
            }

            if (string.Equals(setting.Category, "Layout", StringComparison.OrdinalIgnoreCase))
            {
                plaintext = ApplyLayoutServiceName(plaintext, serviceName);
            }

            var stored = setting.IsSecret
                ? encryptor.Encrypt(plaintext)
                : plaintext;

            newTenant.Settings.Add(new TenantSettingEntity
            {
                Id = Guid.NewGuid(),
                TenantId = newTenantId,
                Category = setting.Category,
                Target = setting.Target,
                Settings = stored,
                IsSecret = setting.IsSecret,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        if (internalAuthTemplate is not null)
        {
            // Same JSON for Api and Web (and any other InternalServiceAuth targets from source).
            var sharedInternalAuthJson = ApplyInternalServiceAuthSecrets(
                internalAuthTemplate,
                internalServiceAuthSecretKey,
                serviceApiKeys);

            internalAuthTargets.Add("Api");
            internalAuthTargets.Add("Web");

            var storedInternalAuth = internalAuthIsSecret
                ? encryptor.Encrypt(sharedInternalAuthJson)
                : sharedInternalAuthJson;

            foreach (var target in internalAuthTargets)
            {
                newTenant.Settings.Add(new TenantSettingEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = newTenantId,
                    Category = "InternalServiceAuth",
                    Target = target,
                    Settings = storedInternalAuth,
                    IsSecret = internalAuthIsSecret,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
        }

        EnsureDuplicatedTenantAuthorizationDefaults(newTenant, newTenantId, authorizationApiSecretKey, now);
        ApplyDuplicatedTenantTestAuthenticationDefaults(newTenant, newTenantId, now);
        EnsureDuplicatedTenantEmptyTemplateBindings(newTenant, now);
        EnsureDuplicatedTenantLayoutServiceName(newTenant, serviceName, now);

        dbContext.Tenants.Add(newTenant);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Duplicated tenant '{SourceName}' ({SourceId}) to '{NewName}' ({NewId}) with {SettingCount} settings, hostname '{Hostname}', origin '{Origin}'. Principals and form templates were not copied. Authorization and InternalServiceAuth secrets were regenerated. Interactive auth defaults to TestAuthentication on Api and Web.",
            source.Name,
            sourceTenantId,
            newTenantName,
            newTenantId,
            newTenant.Settings.Count,
            hostname,
            frontendOrigin);

        return new DuplicateTenantResult(
            sourceTenantId,
            newTenantId,
            newTenantName,
            hostname,
            frontendOrigin,
            newTenant.Settings.Count);
    }

    /// <summary>
    /// Sets the API token signing secret on Authorization category JSON.
    /// Accepts nested <c>TokenSettings</c>, legacy flat <c>SecretKey</c> at the root,
    /// or creates <c>TokenSettings</c> when neither is present.
    /// </summary>
    internal static string ApplyAuthorizationApiSecretKey(
        string settingsJson,
        string secretKey,
        Guid tenantId)
    {
        var root = ParseObject(settingsJson);

        if (root["TokenSettings"] is JsonObject tokenSettings)
        {
            tokenSettings["SecretKey"] = secretKey;
            return root.ToJsonString(JsonWriteOptions);
        }

        if (root["SecretKey"] is not null
            || root["Issuer"] is not null
            || root["Audience"] is not null)
        {
            root["SecretKey"] = secretKey;
            return root.ToJsonString(JsonWriteOptions);
        }

        root["TokenSettings"] = BuildAuthorizationTokenSettingsNode(secretKey, tenantId);
        return root.ToJsonString(JsonWriteOptions);
    }

    internal static string BuildAuthorizationSettingsJson(string secretKey, Guid tenantId)
    {
        var root = new JsonObject
        {
            ["TokenSettings"] = BuildAuthorizationTokenSettingsNode(secretKey, tenantId)
        };

        return root.ToJsonString(JsonWriteOptions);
    }

    private static JsonObject BuildAuthorizationTokenSettingsNode(string secretKey, Guid tenantId) =>
        new()
        {
            ["SecretKey"] = secretKey,
            ["Issuer"] = tenantId.ToString(),
            ["Audience"] = $"api-audience-{tenantId:D}",
            ["TokenLifetimeMinutes"] = 60
        };

    private void EnsureDuplicatedTenantAuthorizationDefaults(
        TenantEntity newTenant,
        Guid newTenantId,
        string authorizationApiSecretKey,
        DateTime now)
    {
        var hasApiAuthorization = newTenant.Settings.Any(s =>
            string.Equals(s.Category, "Authorization", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(s.Target, "Api", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(s.Target, "Shared", StringComparison.OrdinalIgnoreCase)));

        if (hasApiAuthorization)
        {
            return;
        }

        UpsertDuplicatedTenantSetting(
            newTenant,
            "Authorization",
            "Api",
            BuildAuthorizationSettingsJson(authorizationApiSecretKey, newTenantId),
            isSecret: true,
            now);
    }

    /// <summary>
    /// Sets root SecretKey and each Services[].ApiKey (matched by Email) on InternalServiceAuth JSON.
    /// Call once, then persist the same string to Api and Web.
    /// </summary>
    internal static string ApplyInternalServiceAuthSecrets(
        string settingsJson,
        string secretKey,
        IReadOnlyDictionary<string, string> serviceApiKeysByEmail)
    {
        var root = ParseObject(settingsJson);
        root["SecretKey"] = secretKey;

        if (root["Services"] is JsonArray services)
        {
            foreach (var node in services)
            {
                if (node is not JsonObject service)
                    continue;

                var email = service["Email"]?.GetValue<string>()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(email))
                    throw new InvalidOperationException(
                        "InternalServiceAuth Services entries must include Email.");

                if (!serviceApiKeysByEmail.TryGetValue(email, out var apiKey) ||
                    string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException(
                        $"InternalServiceAuth ApiKey is required for service '{email}'.");
                }

                if (apiKey.Length < 32)
                {
                    throw new InvalidOperationException(
                        $"InternalServiceAuth ApiKey for service '{email}' must be at least 32 characters.");
                }

                service["ApiKey"] = apiKey;
            }
        }

        return root.ToJsonString(JsonWriteOptions);
    }

    internal static string GenerateSecretKey(int byteLength = 48) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength));

    internal static string BuildTestAuthenticationSettingsJson(Guid tenantId, string signingKey)
    {
        var root = new JsonObject
        {
            ["Enabled"] = true,
            ["JwtSigningKey"] = signingKey,
            ["JwtIssuer"] = tenantId.ToString(),
            ["JwtAudience"] = $"test-audience-{tenantId:D}"
        };

        return root.ToJsonString(JsonWriteOptions);
    }

    internal static string BuildTestAuthenticationSchemeJson() =>
        """{"Scheme":"TestAuthentication"}""";

    private void ApplyDuplicatedTenantTestAuthenticationDefaults(
        TenantEntity newTenant,
        Guid newTenantId,
        DateTime now)
    {
        var testAuthJson = BuildTestAuthenticationSettingsJson(newTenantId, GenerateSecretKey(48));
        var schemeJson = BuildTestAuthenticationSchemeJson();

        UpsertDuplicatedTenantSetting(newTenant, "Authentication", "Web", schemeJson, isSecret: false, now);
        UpsertDuplicatedTenantSetting(newTenant, "TestAuthentication", "Api", testAuthJson, isSecret: true, now);
        UpsertDuplicatedTenantSetting(newTenant, "TestAuthentication", "Web", testAuthJson, isSecret: true, now);
    }

    /// <summary>
    /// Sets <c>ServiceName</c> on Layout category JSON.
    /// </summary>
    internal static string ApplyLayoutServiceName(string settingsJson, string serviceName)
    {
        var root = ParseObject(settingsJson);
        root["ServiceName"] = serviceName;
        return root.ToJsonString(JsonWriteOptions);
    }

    private void EnsureDuplicatedTenantLayoutServiceName(
        TenantEntity newTenant,
        string serviceName,
        DateTime now)
    {
        var hasWebLayout = newTenant.Settings.Any(s =>
            string.Equals(s.Category, "Layout", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(s.Target, "Web", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(s.Target, "Shared", StringComparison.OrdinalIgnoreCase)));

        if (hasWebLayout)
        {
            return;
        }

        UpsertDuplicatedTenantSetting(
            newTenant,
            "Layout",
            "Web",
            ApplyLayoutServiceName("{}", serviceName),
            isSecret: false,
            now);
    }

    /// <summary>
    /// Explicit empty HostMappings so the catalogue does not fall back to every template
    /// in a shared EA database after clone.
    /// </summary>
    private void EnsureDuplicatedTenantEmptyTemplateBindings(TenantEntity newTenant, DateTime now)
    {
        const string emptyHostMappingsJson = """{"HostMappings":{}}""";
        UpsertDuplicatedTenantSetting(newTenant, "ApplicationTemplates", "Api", emptyHostMappingsJson, isSecret: false, now);
        UpsertDuplicatedTenantSetting(newTenant, "Template", "Web", emptyHostMappingsJson, isSecret: false, now);
    }

    internal static bool IsTemplateBindingCategory(string? category) =>
        string.Equals(category, "ApplicationTemplates", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(category, "Template", StringComparison.OrdinalIgnoreCase);

    private void UpsertDuplicatedTenantSetting(
        TenantEntity tenant,
        string category,
        string target,
        string plaintext,
        bool isSecret,
        DateTime now)
    {
        var existing = tenant.Settings.FirstOrDefault(s =>
            string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase));

        var stored = isSecret ? encryptor.Encrypt(plaintext) : plaintext;

        if (existing is not null)
        {
            existing.Settings = stored;
            existing.IsSecret = isSecret;
            existing.UpdatedAtUtc = now;
            return;
        }

        tenant.Settings.Add(new TenantSettingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Category = category,
            Target = target,
            Settings = stored,
            IsSecret = isSecret,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }

    private static IReadOnlyDictionary<string, string> NormalizeServiceApiKeys(
        IReadOnlyList<(string Email, string ApiKey)>? keys)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (keys is null)
            return map;

        foreach (var (email, apiKey) in keys)
        {
            var normalizedEmail = (email ?? string.Empty).Trim();
            var normalizedApiKey = (apiKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                continue;

            map[normalizedEmail] = normalizedApiKey;
        }

        return map;
    }

    private static JsonObject ParseObject(string settingsJson)
    {
        try
        {
            return JsonNode.Parse(settingsJson) as JsonObject
                ?? throw new InvalidOperationException("Settings JSON must be an object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Settings JSON is invalid.", ex);
        }
    }

    internal static string NormalizeHostname(string? value)
    {
        var hostname = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(hostname))
            return string.Empty;

        if (Uri.TryCreate(hostname, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            hostname = uri.Host;
        }

        return hostname.Trim().TrimEnd('/').ToLowerInvariant();
    }

    internal static string NormalizeOrigin(string? value)
    {
        var origin = (value ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(origin))
            return string.Empty;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Frontend origin must be an absolute http(s) URL, for example https://example.education.gov.uk");
        }

        return $"{uri.Scheme}://{uri.Authority}";
    }
}
