using GovUK.Dfe.FlexForms.Domain.Entities;

namespace GovUK.Dfe.FlexForms.Domain.Services;

public interface ITenantAccessAuditQuery
{
    Task<IReadOnlyList<TenantAccessAudit>> ListAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken = default);
}
