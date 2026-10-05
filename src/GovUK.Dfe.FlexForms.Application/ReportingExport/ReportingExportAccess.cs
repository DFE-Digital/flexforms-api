using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport;

/// <summary>Why a reporting export request was refused before reaching Prism.</summary>
public sealed record ReportingExportDenial(DomainErrorCode Code, string Message)
{
    public Result<T> As<T>() => Code == DomainErrorCode.NotFound ? Result<T>.NotFound(Message) : Result<T>.Forbid(Message);
}

/// <summary>
/// Checks that the caller is an interactive admin of the tenant in the route and, when a template is named, that the
/// template belongs to that tenant. Prism trusts the API to have done this.
/// </summary>
public interface IReportingExportAccess
{
    Task<ReportingExportDenial?> CheckAsync(Guid tenantId, Guid? templateId, CancellationToken cancellationToken);

    /// <summary>The signed-in admin, recorded in Prism's audit trail.</summary>
    string ActingUser { get; }
}

public sealed class ReportingExportAccess(
    IPermissionCheckerService permissionChecker,
    ITenantContextAccessor tenantContextAccessor,
    ITenantTemplateResolver tenantTemplateResolver,
    IHttpContextAccessor httpContextAccessor) : IReportingExportAccess
{
    public async Task<ReportingExportDenial?> CheckAsync(Guid tenantId, Guid? templateId, CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return new(DomainErrorCode.Forbidden, "Only tenant administrators can manage reporting export.");
        }

        if (tenantContextAccessor.CurrentTenant is not { } tenant || tenant.Id != tenantId)
        {
            return new(DomainErrorCode.Forbidden, "Administrators can only manage reporting export for their own tenant.");
        }

        if (templateId is { } id && !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(id), cancellationToken))
        {
            return new(DomainErrorCode.NotFound, "Template not found.");
        }

        return null;
    }

    public string ActingUser
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            return new[] { user?.FindFirstValue(ClaimTypes.Email), user?.FindFirstValue("email"), user?.Identity?.Name }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? "unknown";
        }
    }
}
