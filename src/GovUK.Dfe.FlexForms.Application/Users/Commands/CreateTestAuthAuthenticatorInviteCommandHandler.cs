using System.Globalization;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// An administrator creates a one-time code so <see cref="Email"/> can set up a Test Authentication authenticator app
/// without first receiving an emailed password. Anyone holding the code can enrol an app for that email, so only a
/// SuperAdmin may create one for another administrator, and every code is written to the tenant access audit.
/// </summary>
[RateLimit(10, 60)]
public sealed record CreateTestAuthAuthenticatorInviteCommand(string Email)
    : IRequest<Result<CreateTestAuthAuthenticatorInviteResponse>>, IRateLimitedRequest;

public sealed class CreateTestAuthAuthenticatorInviteCommandHandler(
    IHttpContextAccessor httpContextAccessor,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    IPermissionCheckerService permissionCheckerService,
    IEaRepository<User> userRepository,
    ITenantMembershipService tenantMembershipService,
    ITestAuthAuthenticatorInviteService inviteService,
    ITenantAccessAuditWriter accessAuditWriter)
    : IRequestHandler<CreateTestAuthAuthenticatorInviteCommand, Result<CreateTestAuthAuthenticatorInviteResponse>>
{
    public const string AuditAction = "TestAuthAuthenticatorSetupCodeCreated";
    public const string NotAllowedMessage = "Only administrators can create authenticator setup codes.";
    public const string AdministratorTargetMessage =
        "Only a SuperAdmin can create an authenticator setup code for another administrator.";

    public async Task<Result<CreateTestAuthAuthenticatorInviteResponse>> Handle(
        CreateTestAuthAuthenticatorInviteCommand request,
        CancellationToken cancellationToken)
    {
        var tenant = tenantContextAccessor.CurrentTenant;
        if (!TestAuthenticationEnvironmentGate.IsEnabledForTenant(hostEnvironment, tenant))
            return Result<CreateTestAuthAuthenticatorInviteResponse>.Forbid(TestAuthAuthenticatorCaller.NotEnabledMessage);

        if (!permissionCheckerService.CanManageUsers())
            return Result<CreateTestAuthAuthenticatorInviteResponse>.Forbid(NotAllowedMessage);

        var email = request.Email.Trim();
        var actorEmail = EntraClientIdentity.PickEmail(httpContextAccessor.HttpContext?.User)?.Trim();
        var isSelf = string.Equals(actorEmail, email, StringComparison.OrdinalIgnoreCase);

        var subject = await FindUserAsync(email, cancellationToken);
        if (subject is not null
            && !isSelf
            && !permissionCheckerService.IsInteractivePlatformAdmin()
            && await IsAdministratorAsync(subject, tenant!.Id, cancellationToken))
        {
            return Result<CreateTestAuthAuthenticatorInviteResponse>.Forbid(AdministratorTargetMessage);
        }

        var invite = await inviteService.IssueAsync(email);

        var actor = string.IsNullOrEmpty(actorEmail) ? null : await FindUserAsync(actorEmail, cancellationToken);
        await accessAuditWriter.AppendAsync(
            tenant!.Id,
            subject?.Id,
            email,
            AuditAction,
            roleName: null,
            actor?.Id,
            actorEmail ?? httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "unknown",
            $"Authenticator setup code created, expires {invite.ExpiresAtUtc.ToString("u", CultureInfo.InvariantCulture)}",
            cancellationToken);

        return Result<CreateTestAuthAuthenticatorInviteResponse>.Success(
            new CreateTestAuthAuthenticatorInviteResponse(invite.Code, invite.ExpiresAtUtc.UtcDateTime));
    }

    private Task<User?> FindUserAsync(string email, CancellationToken cancellationToken)
        => new GetUserByEmailQueryObject(email)
            .Apply(userRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> IsAdministratorAsync(User user, Guid tenantId, CancellationToken cancellationToken)
    {
        if (RoleNames.IsPlatformSuperAdminUser(user.Role?.Name, user.RoleId.Value))
            return true;

        if (user.Id is null)
            return false;

        var membership = await tenantMembershipService.GetActiveMembershipAsync(tenantId, user.Id, cancellationToken);
        var roleName = membership?.Role?.Name;
        return RoleNames.IsSuperAdmin(roleName)
               || string.Equals(roleName, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
    }
}
