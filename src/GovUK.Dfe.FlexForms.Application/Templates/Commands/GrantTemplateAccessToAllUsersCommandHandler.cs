using System.Security.Claims;
using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Templates.Commands;

/// <summary>
/// Grants Template Read/Write for one tenant template to every active member of the current tenant.
/// </summary>
public sealed record GrantTemplateAccessToAllUsersCommand(Guid TemplateId)
    : IRequest<Result<GrantTemplateAccessToAllUsersResponse>>;

public sealed class GrantTemplateAccessToAllUsersCommandValidator
    : AbstractValidator<GrantTemplateAccessToAllUsersCommand>
{
    public GrantTemplateAccessToAllUsersCommandValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="GrantTemplateAccessToAllUsersCommand"/>.
/// </summary>
public sealed class GrantTemplateAccessToAllUsersCommandHandler(
    IEaRepository<TenantMembership> membershipRepository,
    IEaRepository<User> userRepository,
    IUnitOfWork unitOfWork,
    IUserFactory userFactory,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionCheckerService,
    IUserCacheInvalidator userCacheInvalidator,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<GrantTemplateAccessToAllUsersCommand, Result<GrantTemplateAccessToAllUsersResponse>>
{
    public async Task<Result<GrantTemplateAccessToAllUsersResponse>> Handle(
        GrantTemplateAccessToAllUsersCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.CanManageTemplates())
            return Result<GrantTemplateAccessToAllUsersResponse>.Forbid(
                "Only template administrators can grant template access to all users");

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
            return Result<GrantTemplateAccessToAllUsersResponse>.Forbid("Tenant context is required");

        var templateId = new TemplateId(command.TemplateId);
        if (!await tenantTemplateCatalogue.ContainsAsync(templateId, cancellationToken))
            return Result<GrantTemplateAccessToAllUsersResponse>.NotFound(
                $"Template '{command.TemplateId}' was not found in the current tenant");

        var grantedById = await ResolveGrantedByUserIdAsync(cancellationToken);
        if (grantedById is null)
            return Result<GrantTemplateAccessToAllUsersResponse>.Failure(
                "Could not resolve the acting administrator");

        var memberUserIds = await new GetActiveTenantMembershipsWithUsersQueryObject(currentTenant.Id)
            .Apply(membershipRepository.Query())
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (memberUserIds.Count == 0)
        {
            return Result<GrantTemplateAccessToAllUsersResponse>.Success(
                new GrantTemplateAccessToAllUsersResponse(command.TemplateId, 0, 0, 0));
        }

        var users = await userRepository.Query()
            .Include(u => u.Permissions)
            .Where(u => u.Id != null && memberUserIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var granted = 0;
        var alreadyHad = 0;

        foreach (var user in users)
        {
            if (UserTemplateAccess.HasAccess(user, templateId))
            {
                alreadyHad++;
                continue;
            }

            userFactory.EnsureUserHasTemplatePermission(user, templateId, grantedById, now);
            granted++;
        }

        if (granted > 0)
        {
            await unitOfWork.CommitAsync(cancellationToken);
            await userCacheInvalidator.InvalidateTenantUserClaimsAsync(cancellationToken);
        }

        return Result<GrantTemplateAccessToAllUsersResponse>.Success(
            new GrantTemplateAccessToAllUsersResponse(
                command.TemplateId,
                users.Count,
                granted,
                alreadyHad));
    }

    private async Task<UserId?> ResolveGrantedByUserIdAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var adminUser = await new GetUserByEmailQueryObject(email)
            .Apply(userRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        return adminUser?.Id;
    }
}
