using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace GovUK.Dfe.FlexForms.Domain.Interfaces;

/// <summary>
/// Prism's export control endpoints. Prism owns the export policy, so tenant admins change it through these calls.
/// Every change is recorded in Prism against <c>actingUser</c>.
/// </summary>
public interface IPrismExportControlClient
{
    Task<Result<ReportingExportPolicyDto>> GetPolicyAsync(Guid tenantId, Guid templateId, CancellationToken cancellationToken);

    Task<Result<ReportingExportChangeResultDto>> ChangeDecisionsAsync(
        Guid tenantId,
        Guid templateId,
        IReadOnlyList<ReportingExportDecisionRequest> decisions,
        string actingUser,
        CancellationToken cancellationToken);

    /// <summary>The tenant default when <paramref name="templateId"/> is null, otherwise the template's.</summary>
    Task<Result<ReportingExportDefaultDto>> GetDefaultAsync(Guid tenantId, Guid? templateId, CancellationToken cancellationToken);

    /// <summary>Sets the tenant default when <paramref name="templateId"/> is null, otherwise the template's.</summary>
    Task<Result<ReportingExportChangeResultDto>> ChangeDefaultAsync(
        Guid tenantId,
        Guid? templateId,
        UpdateReportingExportDefaultRequest request,
        string actingUser,
        CancellationToken cancellationToken);

    /// <summary>A refresh started by a change; not found when it belongs to another tenant.</summary>
    Task<Result<ReportingExportRefreshDto>> GetRefreshAsync(Guid tenantId, Guid refreshId, CancellationToken cancellationToken);
}
