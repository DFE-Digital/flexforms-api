using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Persists a new TenantConfig tenant from an explicit list of settings rows.
/// </summary>
public sealed class TenantCreatorService(
    TenantConfigDbContext dbContext,
    ITenantSettingsEncryptor encryptor,
    ILogger<TenantCreatorService> logger) : ITenantCreator
{
    public async Task<CreateTenantResult> CreateAsync(
        NewTenantDefinition tenant,
        CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId == Guid.Empty)
            throw new InvalidOperationException("New tenant id is required.");

        var name = (tenant.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("New tenant name is required.");
        if (name.Length > 100)
            throw new InvalidOperationException("New tenant name must not exceed 100 characters.");

        var hostname = NormalizeHostname(tenant.Hostname);
        if (string.IsNullOrWhiteSpace(hostname))
            throw new InvalidOperationException("Hostname is required.");
        if (hostname.Length > 255)
            throw new InvalidOperationException("Hostname must not exceed 255 characters.");

        var frontendOrigin = NormalizeOrigin(tenant.FrontendOrigin);
        if (string.IsNullOrWhiteSpace(frontendOrigin))
            throw new InvalidOperationException("Frontend origin is required.");
        if (frontendOrigin.Length > 500)
            throw new InvalidOperationException("Frontend origin must not exceed 500 characters.");

        if (tenant.Settings.Count == 0)
            throw new InvalidOperationException("At least one setting is required.");

        var overlaps = TenantSettingTargetOverlap.FindOverlaps(
            tenant.Settings.Select(s => (s.Category, s.Target)));
        if (overlaps.Count > 0)
        {
            throw new TenantSettingTargetConflictException(
                "The new tenant would have settings under both Shared and an app-specific target: " +
                $"{string.Join(", ", overlaps)}. Keep each category either Shared or app-specific (Api/Web).");
        }

        if (await dbContext.Tenants.AnyAsync(t => t.Id == tenant.TenantId, cancellationToken))
            throw new InvalidOperationException($"Tenant id '{tenant.TenantId}' already exists.");

        if (await dbContext.Tenants.AnyAsync(t => t.Name == name, cancellationToken))
            throw new InvalidOperationException($"Tenant name '{name}' is already in use.");

        if (await dbContext.TenantHostnames.AnyAsync(
                h => h.Hostname.ToLower() == hostname, cancellationToken))
            throw new InvalidOperationException($"Hostname '{hostname}' is already assigned to another tenant.");

        if (await dbContext.TenantFrontendOrigins.AnyAsync(
                o => o.Origin == frontendOrigin, cancellationToken))
            throw new InvalidOperationException($"Frontend origin '{frontendOrigin}' is already assigned to another tenant.");

        var now = DateTime.UtcNow;
        var entity = new TenantEntity
        {
            Id = tenant.TenantId,
            Name = name,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        entity.Hostnames.Add(new TenantHostnameEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Hostname = hostname
        });

        entity.FrontendOrigins.Add(new TenantFrontendOriginEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Origin = frontendOrigin
        });

        foreach (var setting in tenant.Settings)
        {
            entity.Settings.Add(new TenantSettingEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                Category = setting.Category,
                Target = setting.Target,
                Settings = setting.IsSecret ? encryptor.Encrypt(setting.SettingsJson) : setting.SettingsJson,
                IsSecret = setting.IsSecret,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        dbContext.Tenants.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created tenant '{Name}' ({TenantId}) with {SettingCount} setting(s), hostname '{Hostname}', origin '{Origin}'.",
            name,
            tenant.TenantId,
            entity.Settings.Count,
            hostname,
            frontendOrigin);

        return new CreateTenantResult(
            tenant.TenantId,
            name,
            hostname,
            frontendOrigin,
            entity.Settings.Count);
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
