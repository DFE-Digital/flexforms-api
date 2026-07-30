using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Reads TenantConfig setting rows and decrypts secret categories for SuperAdmin editing.
/// </summary>
public class TenantSettingsQueryService(
    TenantConfigDbContext dbContext,
    ITenantSettingsEncryptor encryptor,
    ILogger<TenantSettingsQueryService> logger) : ITenantSettingsQuery
{
    /// <inheritdoc />
    public async Task<TenantSettingsList?> ListSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
            return null;

        var rows = await dbContext.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Target)
            .ToListAsync(cancellationToken);

        var settings = rows.Select(row =>
        {
            var json = row.Settings;
            if (row.IsSecret)
            {
                try
                {
                    json = encryptor.Decrypt(row.Settings);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to decrypt setting '{Category}' (Target={Target}) for tenant {TenantId}.",
                        row.Category,
                        row.Target,
                        tenantId);
                    throw;
                }
            }

            return new TenantSettingRow(
                row.Id,
                row.Category,
                row.Target,
                json,
                row.IsSecret,
                row.UpdatedAtUtc);
        }).ToList();

        return new TenantSettingsList(tenant.Id, tenant.Name, settings);
    }
}
