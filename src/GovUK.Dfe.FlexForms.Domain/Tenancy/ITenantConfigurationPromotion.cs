using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

public interface ITenantConfigurationPromotion
{
    Task<ExportTenantConfigurationDto?> ExportAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<ImportTenantConfigurationResultDto> ImportAsync(
        Guid tenantId,
        ImportTenantConfigurationDto bundle,
        string actorEmail,
        CancellationToken cancellationToken = default);
}
