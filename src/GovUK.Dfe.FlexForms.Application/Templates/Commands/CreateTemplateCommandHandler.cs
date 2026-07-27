using System.Security.Claims;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Templates.Commands;

/// <summary>
/// Creates a new template in the current tenant's application database (Admin only).
/// </summary>
[RateLimit(3, 30)]
public sealed record CreateTemplateCommand(
    string Name,
    string? InitialVersionNumber = null,
    string? JsonSchema = null) : IRequest<Result<TemplateDto>>, IRateLimitedRequest;

/// <summary>
/// Handles creation of a tenant template and grants the creating Admin access.
/// </summary>
public sealed class CreateTemplateCommandHandler(
    IEaRepository<Template> templateRepository,
    IEaRepository<User> userRepository,
    IHttpContextAccessor httpContextAccessor,
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITemplateFactory templateFactory,
    IUserFactory userFactory,
    IUserCacheInvalidator userCacheInvalidator,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTemplateCommand, Result<TemplateDto>>
{
    public async Task<Result<TemplateDto>> Handle(
        CreateTemplateCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!permissionCheckerService.CanManageTemplates())
            {
                return Result<TemplateDto>.Forbid("Only Admin users or roles with Template Manage permission can create templates.");
            }

            var tenant = tenantContextAccessor.CurrentTenant;
            if (tenant is null)
            {
                return Result<TemplateDto>.Forbid("Tenant context is required to create a template.");
            }

            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal principal || principal.Identity?.IsAuthenticated != true)
            {
                return Result<TemplateDto>.Forbid("Not authenticated");
            }

            var principalId = principal.FindFirstValue(ClaimTypes.Email)
                ?? principal.FindFirstValue("appid")
                ?? principal.FindFirstValue("azp");

            if (string.IsNullOrWhiteSpace(principalId))
            {
                return Result<TemplateDto>.Forbid("No user identifier");
            }

            User? dbUser;
            if (principalId.Contains('@'))
            {
                dbUser = await new GetUserWithAllTemplatePermissionsQueryObject(principalId)
                    .Apply(userRepository.Query())
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                dbUser = await new GetUserWithAllTemplatePermissionsByExternalIdQueryObject(principalId)
                    .Apply(userRepository.Query())
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (dbUser is null)
            {
                return Result<TemplateDto>.NotFound("User not found");
            }

            string? decodedSchema = null;
            if (!string.IsNullOrWhiteSpace(request.JsonSchema))
            {
                try
                {
                    var bytes = Convert.FromBase64String(request.JsonSchema);
                    decodedSchema = System.Text.Encoding.UTF8.GetString(bytes);
                }
                catch (FormatException)
                {
                    return Result<TemplateDto>.Failure("Invalid Base64 format for JsonSchema");
                }
            }

            var template = templateFactory.CreateTemplate(request.Name, dbUser.Id!, tenant.Id);
            string? latestVersion = null;

            if (!string.IsNullOrWhiteSpace(request.InitialVersionNumber) && decodedSchema is not null)
            {
                var version = templateFactory.AddVersionToTemplate(
                    template,
                    request.InitialVersionNumber,
                    decodedSchema,
                    dbUser.Id!);
                latestVersion = version.VersionNumber;
            }

            await templateRepository.AddAsync(template, cancellationToken);

            userFactory.EnsureUserHasTemplatePermission(
                dbUser,
                template.Id!,
                dbUser.Id!);

            await unitOfWork.CommitAsync(cancellationToken);
            await userCacheInvalidator.InvalidateForUserAsync(
                dbUser.Email,
                dbUser.ExternalProviderId,
                dbUser.Id!,
                cancellationToken);

            return Result<TemplateDto>.Success(new TemplateDto
            {
                TemplateId = template.Id!.Value,
                Name = template.Name,
                CreatedOn = template.CreatedOn,
                LatestVersionNumber = latestVersion,
                IsLive = template.IsLive
            });
        }
        catch (ArgumentException ex)
        {
            return Result<TemplateDto>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<TemplateDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<TemplateDto>.Failure(ex.ToString());
        }
    }
}
