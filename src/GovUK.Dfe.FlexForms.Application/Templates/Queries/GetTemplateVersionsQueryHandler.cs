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

public sealed record GetTemplateVersionsQuery(Guid TemplateId)
    : IRequest<Result<IReadOnlyCollection<TemplateVersionSummaryDto>>>;

public sealed class GetTemplateVersionsQueryHandler(
    IHttpContextAccessor httpContextAccessor,
    IEaRepository<User> userRepo,
    IEaRepository<TemplateVersion> versionRepo,
    IPermissionCheckerService permissionCheckerService,
    ITenantTemplateResolver tenantTemplateResolver)
    : IRequestHandler<GetTemplateVersionsQuery, Result<IReadOnlyCollection<TemplateVersionSummaryDto>>>
{
    public async Task<Result<IReadOnlyCollection<TemplateVersionSummaryDto>>> Handle(
        GetTemplateVersionsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not { } user || user.Identity?.IsAuthenticated != true)
                return Result<IReadOnlyCollection<TemplateVersionSummaryDto>>.Failure("Not authenticated");

            var principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");
            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(principalId))
                return Result<IReadOnlyCollection<TemplateVersionSummaryDto>>.Forbid("No user identifier");

            var templateId = new TemplateId(request.TemplateId);
            if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))
                return Result<IReadOnlyCollection<TemplateVersionSummaryDto>>.Forbid(
                    "Template does not belong to the current tenant");

            if (principalId.Contains('@'))
            {
                var dbUser = await new GetUserByEmailQueryObject(principalId)
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
                if (dbUser is null)
                    return Result<IReadOnlyCollection<TemplateVersionSummaryDto>>.NotFound("User not found");
            }
            else
            {
                var dbUser = await new GetUserByExternalProviderIdQueryObject(principalId)
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
                if (dbUser is null)
                    return Result<IReadOnlyCollection<TemplateVersionSummaryDto>>.NotFound("User not found");
            }

            if (!permissionCheckerService.HasPermission(
                    ResourceType.Template,
                    request.TemplateId.ToString(),
                    AccessType.Read))
            {
                return Result<IReadOnlyCollection<TemplateVersionSummaryDto>>.Forbid(
                    "User does not have permission to read this template");
            }

            var versions = await new GetTemplateVersionsForTemplateQueryObject(templateId)
                .Apply(versionRepo.Query().AsNoTracking())
                .Select(tv => new TemplateVersionSummaryDto
                {
                    TemplateId = tv.TemplateId.Value,
                    TemplateVersionId = tv.Id!.Value,
                    VersionNumber = tv.VersionNumber,
                    CreatedOn = tv.CreatedOn
                })
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyCollection<TemplateVersionSummaryDto>>.Success(versions);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyCollection<TemplateVersionSummaryDto>>.Failure(ex.ToString());
        }
    }
}
