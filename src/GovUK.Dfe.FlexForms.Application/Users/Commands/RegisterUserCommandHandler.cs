using System.IdentityModel.Tokens.Jwt;

using System.Security.Claims;

using GovUK.Dfe.FlexForms.Application.Common.Attributes;

using GovUK.Dfe.FlexForms.Application.Common.Behaviours;

using GovUK.Dfe.FlexForms.Application.Security;

using GovUK.Dfe.FlexForms.Application.Services;

using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;

using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;

using GovUK.Dfe.FlexForms.Domain.Common;

using GovUK.Dfe.FlexForms.Domain.Entities;

using GovUK.Dfe.FlexForms.Domain.Factories;

using GovUK.Dfe.FlexForms.Domain.Interfaces;

using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;

using GovUK.Dfe.FlexForms.Domain.Services;

using GovUK.Dfe.FlexForms.Domain.Tenancy;

using GovUK.Dfe.FlexForms.Domain.ValueObjects;

using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

using GovUK.Dfe.CoreLibs.Security.Configurations;

using GovUK.Dfe.CoreLibs.Security.Interfaces;

using MediatR;

using Microsoft.AspNetCore.Http;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.Hosting;

using Microsoft.IdentityModel.Tokens;



namespace GovUK.Dfe.FlexForms.Application.Users.Commands;



[RateLimit(5, 30)]

public sealed record RegisterUserCommand(string SubjectToken, Guid? TemplateId = null) : IRequest<Result<UserDto>>, IRateLimitedRequest;



public sealed class RegisterUserCommandHandler(

    IEaRepository<User> userRepo,

    IEaRepository<Template> templateRepo,

    IExternalIdentityValidator externalValidator,

    IHttpContextAccessor httpContextAccessor,

    IUserFactory userFactory,

    IUnitOfWork unitOfWork,

    ITenantContextAccessor tenantContextAccessor,

    ITenantTemplateResolver tenantTemplateResolver,

    ITenantMembershipService tenantMembershipService,

    ITenantOidcAudienceBinder tenantOidcAudienceBinder,

    ISelfRegistrationTemplateAccessService selfRegistrationTemplateAccess,

    IUserCacheInvalidator userCacheInvalidator,

    IHostEnvironment hostEnvironment) : IRequestHandler<RegisterUserCommand, Result<UserDto>>

{

    private sealed record TemplateResolution(

        bool IsSuccess,

        TemplateId? TemplateId,

        string? Error,

        DomainErrorCode? ErrorCode)

    {

        public static TemplateResolution Ok(TemplateId? templateId) =>

            new(true, templateId, null, null);



        public static TemplateResolution Fail(DomainErrorCode code, string error) =>

            new(false, null, error, code);

    }



    public async Task<Result<UserDto>> Handle(

        RegisterUserCommand request,

        CancellationToken cancellationToken)

    {

        try

        {

            // Get tenant-specific test auth options so only the correct tenant uses test authentication.
            // Production hard-blocks Test Authentication regardless of tenant settings.

            TestAuthenticationOptions? tenantTestAuthOptions = null;

            if (tenantContextAccessor.CurrentTenant != null

                && TestAuthenticationEnvironmentGate.IsAllowed(hostEnvironment))

            {

                tenantTestAuthOptions = new TestAuthenticationOptions();

                tenantContextAccessor.CurrentTenant.Settings

                    .GetSection(TestAuthenticationOptions.SectionName)

                    .Bind(tenantTestAuthOptions);

            }



            var testOptsForValidation = TestSubjectTokenDetector.ForTokenValidation(

                tenantTestAuthOptions,

                request.SubjectToken);



            // Validate external token and extract claims

            var externalUser = await externalValidator

                .ValidateIdTokenAsync(request.SubjectToken, false, false, internalAuthOptions: null, testOptsForValidation, cancellationToken);



            var email = externalUser.FindFirst(ClaimTypes.Email)?.Value

                        ?? throw new SecurityTokenException("RegisterUserCommandHandler > Missing email");



            var currentTenant = tenantContextAccessor.CurrentTenant

                ?? throw new SecurityTokenException("RegisterUserCommandHandler > Tenant could not be resolved");



            var useTestAuth = TestSubjectTokenDetector.IsActiveTestSubjectToken(

                tenantTestAuthOptions,

                request.SubjectToken);

            if (!useTestAuth

                && !tenantOidcAudienceBinder.TokenMatchesTenant(currentTenant, ReadTokenAudiences(request.SubjectToken)))

            {

                return Result<UserDto>.Forbid(

                    "The identity token was not issued for this tenant. " +

                    "Check X-Tenant-ID matches the application you signed in to.");

            }



            var fullName = $"{externalUser.FindFirst(ClaimTypes.GivenName)?.Value} {externalUser.FindFirst(ClaimTypes.Surname)?.Value}";



            var name = externalUser.FindFirst("name")?.Value

                       ?? externalUser.FindFirst("given_name")?.Value

                       ?? email; // Fallback to email if name not available



            if (string.IsNullOrWhiteSpace(fullName))

                fullName = name;



            var now = DateTime.UtcNow;



            // Load user by email with template permissions to check access

            var dbUser = await new GetUserWithAllPermissionsByEmailQueryObject(email)

                .Apply(userRepo.Query().AsNoTracking())

                .FirstOrDefaultAsync(cancellationToken: cancellationToken);



            if (dbUser is not null)

            {

                var existingUser = await new GetUserWithAllPermissionsByEmailQueryObject(email)

                    .Apply(userRepo.Query())

                    .FirstOrDefaultAsync(cancellationToken: cancellationToken);



                if (existingUser?.Id is null)

                    return Result<UserDto>.Failure("User not found for registration update");



                var changed = false;



                // Explicit template grant when requested

                if (request.TemplateId.HasValue)

                {

                    var explicitGrant = await TryResolveExplicitTemplateAsync(

                        request.TemplateId.Value,

                        cancellationToken);



                    if (!explicitGrant.IsSuccess)

                        return MapFailure(explicitGrant);



                    if (explicitGrant.TemplateId is not null

                        && !SelfRegistrationAccessRules.HasTemplateAccess(existingUser, explicitGrant.TemplateId))

                    {

                        userFactory.EnsureUserHasTemplatePermission(

                            existingUser,

                            explicitGrant.TemplateId,

                            existingUser.Id,

                            now);

                        changed = true;

                    }

                }

                else

                {

                    // Auto-register: grant Template R/W for every live template in the tenant catalogue.

                    if (await selfRegistrationTemplateAccess.EnsureLiveTemplateAccessAsync(

                            existingUser, cancellationToken))

                        changed = true;

                }



                // TenantMembership is required for token exchange. Create User membership when missing.

                if (await EnsureMembershipForCurrentTenantAsync(currentTenant.Id, existingUser, cancellationToken))

                    changed = true;



                if (changed)

                {

                    await unitOfWork.CommitAsync(cancellationToken);

                    await InvalidateUserCachesAsync(existingUser, cancellationToken);

                }



                return Result<UserDto>.Success(MapUser(existingUser));

            }



            // New user: resolve which template (if any) to grant

            TemplateId? primaryTemplate = null;

            if (request.TemplateId.HasValue)

            {

                var templateToAssignResult = await ResolveTemplateForNewUserAsync(request.TemplateId, cancellationToken);

                if (!templateToAssignResult.IsSuccess)

                    return MapFailure(templateToAssignResult);

                primaryTemplate = templateToAssignResult.TemplateId;

            }



            var userId = new UserId(Guid.NewGuid());

            var newUser = userFactory.CreateUser(

                userId,

                new RoleId(RoleConstants.UserRoleId),

                fullName,

                email,

                primaryTemplate,

                now);



            // Auto-register without an explicit template: grant all live tenant forms.

            if (!request.TemplateId.HasValue)

            {

                await selfRegistrationTemplateAccess.EnsureLiveTemplateAccessAsync(newUser, cancellationToken);

            }



            await userRepo.AddAsync(newUser, cancellationToken);

            await tenantMembershipService.UpsertMembershipAsync(

                currentTenant.Id,

                userId,

                RoleNames.User,

                cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);

            await InvalidateUserCachesAsync(newUser, cancellationToken);



            return Result<UserDto>.Success(MapUser(newUser));

        }

        catch (SecurityTokenException ex)

        {

            return Result<UserDto>.Failure($"Invalid token: {ex.Message}");

        }

        catch (Exception ex)

        {

            return Result<UserDto>.Failure(ex.Message);

        }

    }



    /// <summary>

    /// For new users with an explicit TemplateId: validate it is live and in the tenant.

    /// </summary>

    private async Task<TemplateResolution> ResolveTemplateForNewUserAsync(

        Guid? requestedTemplateId,

        CancellationToken cancellationToken)

    {

        if (!requestedTemplateId.HasValue)

            return TemplateResolution.Ok(null);



        return await TryResolveExplicitTemplateAsync(requestedTemplateId.Value, cancellationToken);

    }



    private async Task<TemplateResolution> TryResolveExplicitTemplateAsync(

        Guid templateGuid,

        CancellationToken cancellationToken)

    {

        var templateId = new TemplateId(templateGuid);



        if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))

        {

            return TemplateResolution.Fail(DomainErrorCode.Forbidden, "Template does not belong to the current tenant");

        }



        var templateEntity = await new GetTemplateByIdQueryObject(templateId)

            .Apply(templateRepo.Query().AsNoTracking())

            .FirstOrDefaultAsync(cancellationToken);



        if (templateEntity is null)

        {

            return TemplateResolution.Fail(DomainErrorCode.NotFound, "Template not found");

        }



        if (!templateEntity.IsLive)

        {

            return TemplateResolution.Fail(DomainErrorCode.Forbidden, "Template is not live");

        }



        return TemplateResolution.Ok(templateId);

    }



    private static Result<UserDto> MapFailure(TemplateResolution source) =>

        source.ErrorCode switch

        {

            DomainErrorCode.NotFound => Result<UserDto>.NotFound(source.Error ?? "Not found"),

            DomainErrorCode.Forbidden => Result<UserDto>.Forbid(source.Error ?? "Forbidden"),

            DomainErrorCode.Conflict => Result<UserDto>.Conflict(source.Error ?? "Conflict"),

            DomainErrorCode.Validation => Result<UserDto>.Validation(source.Error ?? "Validation failed"),

            _ => Result<UserDto>.Failure(source.Error ?? "Request failed")

        };



    private static UserDto MapUser(User user) =>

        new()

        {

            UserId = user.Id!.Value,

            Name = user.Name,

            Email = user.Email,

            RoleId = user.RoleId.Value,

            Authorization = CreateAuthorizationFromUser(user)

        };



    private static UserAuthorizationDto? CreateAuthorizationFromUser(User user)

    {

        if (user.Permissions == null || !user.Permissions.Any())

            return null;



        return new UserAuthorizationDto

        {

            Permissions = user.Permissions

                .Select(p => new UserPermissionDto

                {

                    ApplicationId = p.ApplicationId?.Value,

                    ResourceType = p.ResourceType,

                    ResourceKey = p.ResourceKey,

                    AccessType = p.AccessType

                })

                .ToArray(),

            Roles = new List<string> { user.Role?.Name ?? "User" }

        };

    }



    /// <summary>

    /// Ensures an active TenantMembership exists on the aggregate via the application service.

    /// Self-registration always grants the tenant User role — admins elevate via AssignUserRole.

    /// Returns true when a membership was created.

    /// </summary>

    private async Task<bool> EnsureMembershipForCurrentTenantAsync(

        Guid tenantId,

        User user,

        CancellationToken cancellationToken)

    {

        if (user.Id is null)

            return false;



        var existing = await tenantMembershipService.GetActiveMembershipAsync(

            tenantId,

            user.Id,

            cancellationToken);

        if (existing is not null)

            return false;



        // Persistence orchestration only — TenantMembership.CreateSelfRegisteredUser is used inside the service.

        await tenantMembershipService.UpsertMembershipAsync(

            tenantId,

            user.Id,

            RoleNames.User,

            cancellationToken);

        return true;

    }



    private async Task InvalidateUserCachesAsync(User user, CancellationToken cancellationToken)

    {

        if (user.Id is null)

            return;



        await userCacheInvalidator.InvalidateForUserAsync(

            user.Email,

            user.ExternalProviderId,

            user.Id,

            cancellationToken);

    }



    private static IEnumerable<string> ReadTokenAudiences(string subjectToken)

    {

        try

        {

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(subjectToken);

            return jwt.Audiences ?? Enumerable.Empty<string>();

        }

        catch

        {

            return Enumerable.Empty<string>();

        }

    }

}


