using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

public sealed class TenantAccessAuditQueryService(ExternalApplicationsContext dbContext)
    : ITenantAccessAuditQuery
{
    public async Task<IReadOnlyList<TenantAccessAudit>> ListAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);

        return await dbContext.TenantAccessAudits
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
