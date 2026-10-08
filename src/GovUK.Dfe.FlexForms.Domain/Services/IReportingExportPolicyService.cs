using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// The reporting export policy held by reporting (Prism): which answers of a tenant's templates are exported.
/// Every change is recorded in reporting against <c>actorEmail</c> and starts a refresh of the tenant's exported data.
/// </summary>
/// <remarks>
/// Methods return null when reporting does not know the template or refresh. They throw
/// <see cref="ArgumentException"/> when reporting rejects a change, and <see cref="InvalidOperationException"/>
/// when reporting is not set up, refuses FlexForms, or cannot be reached; both messages are fit to show an admin.
/// </remarks>
public interface IReportingExportPolicyService
{
    Task<ReportingExportPolicyDto?> GetPolicyAsync(
        Guid tenantId,
        Guid templateId,
        CancellationToken cancellationToken = default);

    Task<ReportingExportChangeResultDto?> ChangeDecisionsAsync(
        Guid tenantId,
        Guid templateId,
        IReadOnlyList<ReportingExportDecisionRequest> decisions,
        string actorEmail,
        CancellationToken cancellationToken = default);

    /// <summary>The tenant default when <paramref name="templateId"/> is null, otherwise the template's.</summary>
    Task<ReportingExportDefaultDto?> GetDefaultAsync(
        Guid tenantId,
        Guid? templateId,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the tenant default when <paramref name="templateId"/> is null, otherwise the template's.</summary>
    Task<ReportingExportChangeResultDto?> ChangeDefaultAsync(
        Guid tenantId,
        Guid? templateId,
        UpdateReportingExportDefaultRequest request,
        string actorEmail,
        CancellationToken cancellationToken = default);

    /// <summary>A refresh started by a change; null when it does not exist or belongs to another tenant.</summary>
    Task<ReportingExportRefreshDto?> GetRefreshAsync(
        Guid tenantId,
        Guid refreshId,
        CancellationToken cancellationToken = default);
}
