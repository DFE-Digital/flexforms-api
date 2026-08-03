using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Templates.Queries;

public sealed record GetTemplateSchemaByVersionQuery(Guid TemplateId, string VersionNumber)
    : IRequest<Result<TemplateSchemaDto>>;

public sealed class GetTemplateSchemaByVersionQueryHandler(
    IHttpContextAccessor httpContextAccessor,
    IEaRepository<User> userRepo,
    IEaRepository<TemplateVersion> versionRepo,
    IPermissionCheckerService permissionCheckerService,
    ITenantTemplateResolver tenantTemplateResolver)
    : IRequestHandler<GetTemplateSchemaByVersionQuery, Result<TemplateSchemaDto>>
{
    public async Task<Result<TemplateSchemaDto>> Handle(
        GetTemplateSchemaByVersionQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.VersionNumber))
                return Result<TemplateSchemaDto>.Validation("Version number is required");

            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not { } user || user.Identity?.IsAuthenticated != true)
                return Result<TemplateSchemaDto>.Failure("Not authenticated");

            var principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");
            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(principalId))
                return Result<TemplateSchemaDto>.Forbid("No user identifier");

            var templateId = new TemplateId(request.TemplateId);
            if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))
                return Result<TemplateSchemaDto>.Forbid("Template does not belong to the current tenant");

            if (principalId.Contains('@'))
            {
                var dbUser = await new GetUserByEmailQueryObject(principalId)
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
                if (dbUser is null)
                    return Result<TemplateSchemaDto>.NotFound("User not found");
            }
            else
            {
                var dbUser = await new GetUserByExternalProviderIdQueryObject(principalId)
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
                if (dbUser is null)
                    return Result<TemplateSchemaDto>.NotFound("User not found");
            }

            if (!permissionCheckerService.HasPermission(
                    ResourceType.Template,
                    request.TemplateId.ToString(),
                    AccessType.Read))
            {
                return Result<TemplateSchemaDto>.Forbid("User does not have permission to read this template");
            }

            var version = await new GetTemplateVersionByNumberQueryObject(templateId, request.VersionNumber.Trim())
                .Apply(versionRepo.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);

            if (version is null)
                return Result<TemplateSchemaDto>.NotFound("Template version not found");

            return Result<TemplateSchemaDto>.Success(new TemplateSchemaDto
            {
                TemplateId = version.TemplateId.Value,
                TemplateVersionId = version.Id!.Value,
                VersionNumber = version.VersionNumber,
                JsonSchema = version.JsonSchema
            });
        }
        catch (Exception ex)
        {
            return Result<TemplateSchemaDto>.Failure(ex.ToString());
        }
    }
}
