using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

public sealed class TenantSettingAuditQueryService(TenantConfigDbContext dbContext) : ITenantSettingAuditQuery
{
    public async Task<GetTenantSettingAuditLogDto?> ListAsync(
        Guid tenantId,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var tenantExists = await dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Id == tenantId, cancellationToken);

        if (!tenantExists)
        {
            return null;
        }

        var rows = await dbContext.TenantSettingAudits
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.ChangedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(a => new TenantSettingAuditEntryDto(
                a.Id,
                a.Category,
                a.Target,
                a.Action,
                a.ActorEmail,
                a.ChangedAtUtc,
                a.WasSecret))
            .ToListAsync(cancellationToken);

        return new GetTenantSettingAuditLogDto(tenantId, rows);
    }
}
