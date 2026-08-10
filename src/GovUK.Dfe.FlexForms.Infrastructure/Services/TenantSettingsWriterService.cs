using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Persists tenant settings to the TenantConfig database, encrypting secret categories via ITenantSettingsEncryptor.
/// </summary>
public class TenantSettingsWriterService(
    TenantConfigDbContext dbContext,
    ITenantSettingsEncryptor encryptor,
    ILogger<TenantSettingsWriterService> logger) : ITenantSettingsWriter
{
    /// <inheritdoc />
    public async Task<UpsertTenantSettingResult> UpsertSettingAsync(
        Guid tenantId,
        string category,
        string target,
        string settingsJson,
        bool isSecret,
        CancellationToken cancellationToken = default)
    {
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Tenant '{tenantId}' not found.");

        var jsonToStore = isSecret ? encryptor.Encrypt(settingsJson) : settingsJson;

        var existing = await dbContext.TenantSettings
            .FirstOrDefaultAsync(s =>
                s.TenantId == tenantId &&
                s.Category == category &&
                s.Target == target, cancellationToken);

        bool wasCreated;

        if (existing is not null)
        {
            existing.Settings = jsonToStore;
            existing.IsSecret = isSecret;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            wasCreated = false;

            logger.LogInformation(
                "Updated setting '{Category}' (Target={Target}) for tenant '{TenantName}' ({TenantId}).",
                category, target, tenant.Name, tenantId);
        }
        else
        {
            existing = new TenantSettingEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Category = category,
                Target = target,
                Settings = jsonToStore,
                IsSecret = isSecret,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            dbContext.TenantSettings.Add(existing);
            wasCreated = true;

            logger.LogInformation(
                "Created setting '{Category}' (Target={Target}) for tenant '{TenantName}' ({TenantId}).",
                category, target, tenant.Name, tenantId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpsertTenantSettingResult(existing.Id, wasCreated, category, target);
    }

    /// <inheritdoc />
    public async Task<DeleteTenantSettingResult?> DeleteSettingAsync(
        Guid tenantId,
        string category,
        string target,
        CancellationToken cancellationToken = default)
    {
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Tenant '{tenantId}' not found.");

        var existing = await dbContext.TenantSettings
            .FirstOrDefaultAsync(s =>
                s.TenantId == tenantId &&
                s.Category == category &&
                s.Target == target, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        var result = new DeleteTenantSettingResult(existing.Id, existing.Category, existing.Target, existing.IsSecret);
        dbContext.TenantSettings.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Deleted setting '{Category}' (Target={Target}) for tenant '{TenantName}' ({TenantId}).",
            category, target, tenant.Name, tenantId);

        return result;
    }
}
