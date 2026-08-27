using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using System.Text;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Applications.Queries;

public sealed record GenerateApplicationPreviewHtmlQuery(string ApplicationReference) : IRequest<Result<DownloadFileResult>>;

public sealed class GenerateApplicationPreviewHtmlQueryHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    IHttpContextAccessor httpContextAccessor,
    IPermissionCheckerService permissionCheckerService,
    IStaticHtmlGeneratorService htmlGeneratorService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantPermissionFilter tenantPermissionFilter)
    : IRequestHandler<GenerateApplicationPreviewHtmlQuery, Result<DownloadFileResult>>
{
    public async Task<Result<DownloadFileResult>> Handle(
        GenerateApplicationPreviewHtmlQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                return Result<DownloadFileResult>.Forbid("Not authenticated");

            // Get the application by reference
            var application = await (new GetApplicationByReferenceQueryObject(request.ApplicationReference))
                .Apply(applicationRepo.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);

            if (application is null)
                return Result<DownloadFileResult>.NotFound("Application not found");

            if (!await tenantPermissionFilter.ApplicationBelongsToCurrentTenantAsync(
                    application.Id!.Value,
                    cancellationToken))
            {
                return Result<DownloadFileResult>.NotFound("Application not found");
            }

            // Check if user has permission to read this application
            var canAccess = permissionCheckerService.HasPermission(
                ResourceType.Application,
                application.Id!.Value.ToString(),
                AccessType.Read);

            if (!canAccess)
                return Result<DownloadFileResult>.Forbid("User does not have permission to access this application");

            // Get frontend base URL from configuration
            var tenantConfig = tenantContextAccessor.CurrentTenant?.Settings
                               ?? throw new InvalidOperationException("Tenant configuration has not been resolved for preview generation.");
            var frontendBaseUrl = tenantConfig["FrontendSettings:BaseUrl"];
            if (string.IsNullOrEmpty(frontendBaseUrl))
            {
                return Result<DownloadFileResult>.Failure(
                    "Frontend base URL is not configured. Please set 'FrontendSettings:BaseUrl' in application settings.");
            }

            // Build the preview URL
            var previewUrl = $"{frontendBaseUrl}/applications/{request.ApplicationReference}?preview=true";

            // Get tenant-specific internal service authentication configuration
            var internalAuthConfig = new InternalServiceAuthOptions();
            tenantConfig.GetSection(InternalServiceAuthOptions.SectionName).Bind(internalAuthConfig);
            
            var serviceConfig = internalAuthConfig.Services?.FirstOrDefault();
            if (serviceConfig == null)
            {
                return Result<DownloadFileResult>.Failure(
                    "Internal service authentication is not configured for tenant. Please configure 'InternalServiceAuth:Services' in tenant settings.");
            }

            // Build authentication headers for internal service authentication
            var authenticationHeaders = new Dictionary<string, string>
            {
                { "x-service-email", serviceConfig.Email },
                { "x-service-api-key", serviceConfig.ApiKey },
                { "x-tenant-id", tenantContextAccessor.CurrentTenant.Id.ToString()}
            };

            // Optional: Specify a CSS selector to extract only a specific section of the page
            // For example, to extract only the main content area: ".govuk-grid-row" or "#main-content"
            var contentSelector = tenantConfig["FrontendSettings:PreviewContentSelector"];

            // Generate the static HTML using Playwright
            var htmlContent = await htmlGeneratorService.GenerateStaticHtmlAsync(
                previewUrl,
                authenticationHeaders,
                contentSelector,
                cancellationToken);

            // Convert the HTML string to a stream
            var htmlBytes = Encoding.UTF8.GetBytes(htmlContent);
            var stream = new MemoryStream(htmlBytes);

            // Create the download file result
            var fileName = $"application-{request.ApplicationReference}-preview.html";
            var result = new DownloadFileResult
            {
                FileStream = stream,
                FileName = fileName,
                ContentType = "text/html"
            };

            return Result<DownloadFileResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<DownloadFileResult>.Failure(
                $"Failed to generate application preview HTML: {ex.Message}");
        }
    }
}
