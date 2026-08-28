using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Http.Interfaces;
using GovUK.Dfe.FlexForms.Api.Telemetry;
using static GovUK.Dfe.FlexForms.Api.Telemetry.PiiMasking;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Api.Middleware;

/// <summary>
/// Populates CoreLibs request telemetry plus FlexForms form/application scope after authentication.
/// </summary>
public sealed class RequestTelemetryEnrichmentMiddleware(
    RequestDelegate next,
    ILogger<RequestTelemetryEnrichmentMiddleware> logger)
{
    public const string TemplateIdHeader = "X-Template-Id";
    public const string ApplicationReferenceHeader = "X-Application-Reference";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextAccessor tenantAccessor,
        IRequestTelemetryContext telemetry,
        IFlexFormsRequestScope flexFormsScope,
        ICorrelationContext correlationContext)
    {
        var tenant = tenantAccessor.CurrentTenant;
        if (tenant is not null)
        {
            telemetry.TenantId = tenant.Id.ToString();
            telemetry.TenantName = tenant.Name;
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            telemetry.UserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");
            var rawEmail = context.User.FindFirstValue(ClaimTypes.Email)
                ?? context.User.Identity?.Name;
            telemetry.UserEmail = string.IsNullOrWhiteSpace(rawEmail) ? null : MaskEmail(rawEmail);
        }

        if (context.Request.Headers.TryGetValue(TemplateIdHeader, out var templateHeader)
            && !string.IsNullOrWhiteSpace(templateHeader))
        {
            flexFormsScope.TemplateId = templateHeader.ToString();
        }

        if (context.Request.Headers.TryGetValue(ApplicationReferenceHeader, out var appRefHeader)
            && !string.IsNullOrWhiteSpace(appRefHeader))
        {
            flexFormsScope.ApplicationReference = appRefHeader.ToString();
        }

        if (context.Request.RouteValues.TryGetValue("applicationId", out var applicationId)
            && applicationId is not null)
        {
            flexFormsScope.ApplicationId = applicationId.ToString();
        }

        telemetry.CorrelationId ??= correlationContext.CorrelationId.ToString();
        telemetry.ServiceName = "flexforms-api";

        var scope = new Dictionary<string, object>(telemetry.ToScopeDictionary(), StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in flexFormsScope.ToScopeDictionary())
            scope[kvp.Key] = kvp.Value;

        using (logger.BeginScope(scope))
        {
            await next(context);
        }
    }
}
