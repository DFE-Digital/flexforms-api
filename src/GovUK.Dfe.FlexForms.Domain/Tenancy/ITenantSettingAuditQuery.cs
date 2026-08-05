using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

public interface ITenantSettingAuditQuery
{
    Task<GetTenantSettingAuditLogDto?> ListAsync(
        Guid tenantId,
        int take = 100,
        CancellationToken cancellationToken = default);
}
