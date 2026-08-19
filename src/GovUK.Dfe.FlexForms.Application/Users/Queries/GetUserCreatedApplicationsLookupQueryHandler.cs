using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

public sealed record GetUserCreatedApplicationsLookupQuery(string Email)
    : IRequest<Result<UserCreatedApplicationsLookupDto>>;

public sealed class GetUserCreatedApplicationsLookupQueryHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IEaRepository<User> userRepository,
    IEaRepository<Domain.Entities.Application> applicationRepository,
    IEaRepository<Permission> permissionRepository)
    : IRequestHandler<GetUserCreatedApplicationsLookupQuery, Result<UserCreatedApplicationsLookupDto>>
{
    public async Task<Result<UserCreatedApplicationsLookupDto>> Handle(
        GetUserCreatedApplicationsLookupQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.CanManageUsers())
            return Result<UserCreatedApplicationsLookupDto>.Forbid("Only administrators can look up who a user invited");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<UserCreatedApplicationsLookupDto>.Forbid("Tenant context is required");

        if (string.IsNullOrWhiteSpace(request.Email))
            return Result<UserCreatedApplicationsLookupDto>.Failure("Email is required");

        var user = await new GetUserByEmailQueryObject(request.Email)
            .Apply(userRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        if (user?.Id is null)
            return Result<UserCreatedApplicationsLookupDto>.NotFound("User not found");

        var membership = await tenantMembershipService.GetActiveMembershipAsync(
            tenant.Id,
            user.Id,
            cancellationToken);

        if (membership is null)
            return Result<UserCreatedApplicationsLookupDto>.NotFound("User not found");

        var tenantTemplateIds = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        var tenantTemplateIdSet = tenantTemplateIds.ToHashSet();

        var applications = (await new GetApplicationsCreatedByUserQueryObject(user.Id)
            .Apply(applicationRepository.Query())
            .OrderByDescending(a => a.CreatedOn)
            .ToListAsync(cancellationToken))
            .Where(a => a.TemplateVersion != null && tenantTemplateIdSet.Contains(a.TemplateVersion.TemplateId))
            .ToList();

        var applicationIds = applications
            .Where(a => a.Id is not null)
            .Select(a => a.Id!)
            .ToList();

        var invitePermissions = applicationIds.Count == 0
            ? []
            : await new GetApplicationInviteesGrantedByUserQueryObject(user.Id, applicationIds)
                .Apply(permissionRepository.Query())
                .ToListAsync(cancellationToken);

        var inviteesByApplication = invitePermissions
            .Where(p => p.ApplicationId is not null && p.User?.Id is not null)
            .GroupBy(p => p.ApplicationId!)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ApplicationInviteeDto>)g
                    .GroupBy(p => p.UserId)
                    .Select(userGroup =>
                    {
                        var permission = userGroup.OrderBy(p => p.GrantedOn).First();
                        return new ApplicationInviteeDto
                        {
                            UserId = permission.User!.Id!.Value,
                            Name = permission.User.Name,
                            Email = permission.User.Email,
                            GrantedOn = permission.GrantedOn
                        };
                    })
                    .OrderBy(i => i.Email)
                    .ToList());

        var applicationDtos = applications
            .Where(a => a.Id is not null)
            .Select(a => new CreatedApplicationWithInviteesDto
            {
                ApplicationId = a.Id!.Value,
                ApplicationReference = a.ApplicationReference,
                TemplateName = a.TemplateVersion?.Template?.Name ?? string.Empty,
                DateCreated = a.CreatedOn,
                Invitees = inviteesByApplication.TryGetValue(a.Id, out var invitees)
                    ? invitees
                    : []
            })
            .ToList();

        return Result<UserCreatedApplicationsLookupDto>.Success(new UserCreatedApplicationsLookupDto
        {
            UserId = user.Id.Value,
            Name = user.Name,
            Email = user.Email,
            Applications = applicationDtos
        });
    }
}
