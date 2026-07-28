using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries
{
    public record ExchangeTokenQuery(string SubjectToken) : IRequest<Result<ExchangeTokenDto>>;

    public class ExchangeTokenQueryHandler(
        IExternalIdentityValidator externalValidator,
        IEaRepository<User> userRepo,
        IUserTokenServiceFactory tokenServiceFactory,
        IHttpContextAccessor httpCtxAcc,
        ITenantContextAccessor tenantContextAccessor,
        IUserAccessibleTemplateService userAccessibleTemplateService,
        ITenantMembershipService tenantMembershipService,
        ITenantOidcAudienceBinder tenantOidcAudienceBinder,
        [FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        ILogger<ExchangeTokenQueryHandler> logger)
        : IRequestHandler<ExchangeTokenQuery, Result<ExchangeTokenDto>>
    {
        public async Task<Result<ExchangeTokenDto>> Handle(ExchangeTokenQuery req, CancellationToken ct)
        {
            var validInternalAuthReq = internalRequestChecker.IsValidRequest(httpCtxAcc.HttpContext!);

            // Get tenant-specific internal auth options for multi-tenant support
            InternalServiceAuthOptions? tenantInternalAuthOptions = null;
            if (validInternalAuthReq && tenantContextAccessor.CurrentTenant != null)
            {
                tenantInternalAuthOptions = new InternalServiceAuthOptions();
                tenantContextAccessor.CurrentTenant.Settings
                    .GetSection(InternalServiceAuthOptions.SectionName)
                    .Bind(tenantInternalAuthOptions);
            }

            // Get tenant-specific test auth options so only the correct tenant uses test authentication
            TestAuthenticationOptions? tenantTestAuthOptions = null;
            if (tenantContextAccessor.CurrentTenant != null)
            {
                tenantTestAuthOptions = new TestAuthenticationOptions();
                tenantContextAccessor.CurrentTenant.Settings
                    .GetSection(TestAuthenticationOptions.SectionName)
                    .Bind(tenantTestAuthOptions);
            }

            var externalUser = await externalValidator
                .ValidateIdTokenAsync(req.SubjectToken, false, validInternalAuthReq, tenantInternalAuthOptions, tenantTestAuthOptions, ct);

            var email = externalUser.FindFirst(ClaimTypes.Email)?.Value;

            if (email is null)
                return Result<ExchangeTokenDto>.Failure("Missing email");

            if (tenantContextAccessor.CurrentTenant is null)
            {
                logger.LogWarning(
                    "ExchangeToken: No current tenant. Ensure X-Tenant-ID header or Origin is set so tenant resolution can run.");
                return Result<ExchangeTokenDto>.Failure(
                    "Tenant could not be resolved for the current request.");
            }

            var currentTenant = tenantContextAccessor.CurrentTenant;

            // Shared EA DB hardening: ID token audience must belong to THIS tenant's OIDC apps.
            // Skip for internal/test auth paths (no real OIDC audience).
            var useTestOrInternalAuth = validInternalAuthReq
                || tenantTestAuthOptions?.Enabled == true;
            if (!useTestOrInternalAuth
                && !tenantOidcAudienceBinder.TokenMatchesTenant(currentTenant, ReadTokenAudiences(req.SubjectToken)))
            {
                logger.LogWarning(
                    "ExchangeToken: ID token audience does not match tenant {TenantName} ({TenantId}).",
                    currentTenant.Name,
                    currentTenant.Id);
                return Result<ExchangeTokenDto>.Forbid(
                    "The identity token was not issued for this tenant. " +
                    "Check X-Tenant-ID matches the application you signed in to.");
            }

            var dbUser = await new GetUserWithAllPermissionsByEmailQueryObject(email)
                .Apply(userRepo.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken: ct);

            if (dbUser is null)
                return Result<ExchangeTokenDto>.NotFound($"User not found for email {email}");

            if (dbUser.Id is null)
                return Result<ExchangeTokenDto>.Failure($"User {email} has no identifier");

            // Shared EA DB: role for THIS tenant comes from TenantMembership, not global User.RoleId.
            // Exception: platform SuperAdmin (well-known global AdminRoleId / SuperAdmin name) may
            // exchange without a membership so operators are not locked out of FlexForms tenants.
            var membership = await tenantMembershipService.GetActiveMembershipAsync(
                currentTenant.Id,
                dbUser.Id,
                ct);

            string? membershipRoleName;
            if (membership is null)
            {
                var globalRoleName = dbUser.Role?.Name
                    ?? RoleNames.FromRoleId(dbUser.RoleId.Value);

                if (RoleNames.IsPlatformSuperAdminUser(globalRoleName, dbUser.RoleId.Value))
                {
                    membershipRoleName = RoleNames.SuperAdmin;
                    logger.LogInformation(
                        "ExchangeToken: Platform SuperAdmin {Email} allowed without TenantMembership for tenant {TenantName} ({TenantId}).",
                        email,
                        currentTenant.Name,
                        currentTenant.Id);
                }
                else
                {
                    logger.LogWarning(
                        "ExchangeToken: User {Email} has no active membership for tenant {TenantName} ({TenantId}).",
                        email,
                        currentTenant.Name,
                        currentTenant.Id);
                    return Result<ExchangeTokenDto>.Forbid(
                        $"User is not a member of tenant '{currentTenant.Name}'.");
                }
            }
            else
            {
                membershipRoleName = membership.Role?.Name
                    ?? RoleNames.FromRoleId(membership.RoleId.Value)
                    ?? dbUser.Role?.Name;

                // Membership pointing at the well-known global admin RoleId is platform SuperAdmin.
                if (RoleNames.IsPlatformSuperAdminRoleId(membership.RoleId.Value))
                    membershipRoleName = RoleNames.SuperAdmin;
            }

            if (string.IsNullOrWhiteSpace(membershipRoleName))
                return Result<ExchangeTokenDto>.Conflict($"User {email} has no role assigned for this tenant");

            // Multi-template tenants: users may exist with no form access yet (pending admin grant).
            // Allow token exchange so the web app can show the no-access page.
            var accessibleTemplates = await userAccessibleTemplateService.GetAccessibleTemplateIdsAsync(
                dbUser.TemplatePermissions,
                ct);

            if (accessibleTemplates.Count == 0)
            {
                logger.LogInformation(
                    "ExchangeToken: User {Email} has no accessible templates for tenant {TenantName}. TemplatePermissionCount={PermissionCount}. Allowing login without form access.",
                    email,
                    currentTenant.Name,
                    dbUser.TemplatePermissions.Count);
            }

            // Caller was already authenticated by the API pipeline (ServiceCallers → CompositeScheme →
            // TenantBearer, etc.). There is no legacy "AzureEntra" scheme; Entra roles live on User.
            var httpCtx = httpCtxAcc.HttpContext!;
            var requestPrincipal = httpCtx.User;
            var svcRoles = requestPrincipal.Identity?.IsAuthenticated == true
                ? requestPrincipal.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "roles")
                : Enumerable.Empty<Claim>();

            // Create new identity with only specific claims from external user
            var identity = new ClaimsIdentity();

            // SaaS: stamp tenant_id on the issued internal JWT so cross-tenant replay can be
            // rejected by JwtBearer validation downstream.
            identity.AddClaim(new Claim(TenantAuthClaimTypes.TenantId, currentTenant.Id.ToString()));

            var allowedClaimTypes = new[]
            {
                ClaimTypes.NameIdentifier,
                ClaimTypes.Email,
                ClaimTypes.GivenName,
                ClaimTypes.Surname,
                "organisation"
            };

            // Add allowed external claims if not already present
            foreach (var claim in externalUser.Claims)
            {
                if (allowedClaimTypes.Contains(claim.Type) &&
                    !identity.HasClaim(c => c.Type == claim.Type && c.Value == claim.Value))
                {
                    identity.AddClaim(claim);
                }
            }

            // Role claim from tenant membership (not the global User.RoleId).
            if (!identity.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == membershipRoleName))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, membershipRoleName));
            }

            // Merge Entra / app roles from the authenticated request principal, avoiding duplicates
            foreach (var svcRole in svcRoles)
            {
                var isExcludedRole =
                    (svcRole.Type == ClaimTypes.Role || svcRole.Type == "roles") &&
                    (svcRole.Value.Equals(RoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase) ||
                     svcRole.Value.Equals(RoleNames.Admin, StringComparison.OrdinalIgnoreCase) ||
                     svcRole.Value.Equals(RoleNames.User, StringComparison.OrdinalIgnoreCase) ||
                     svcRole.Value.Equals(RoleNames.Caseworker, StringComparison.OrdinalIgnoreCase));

                if (isExcludedRole)
                    continue;

                if (!identity.HasClaim(c => c.Type == svcRole.Type && c.Value == svcRole.Value))
                {
                    identity.AddClaim(svcRole);
                }
            }

            var mergedUser = new ClaimsPrincipal(identity);

            // SaaS: resolve a per-tenant IUserTokenService so the issued JWT is signed with
            // THIS tenant's signing key (not a global key shared by all tenants).
            var tokenSvc = tokenServiceFactory.GetService(currentTenant.Id.ToString());
            var internalToken = await tokenSvc.GetUserTokenModelAsync(mergedUser);

            return Result<ExchangeTokenDto>.Success(new ExchangeTokenDto
            {
                AccessToken = internalToken.AccessToken,
                TokenType = "Bearer",
                ExpiresIn = internalToken.ExpiresIn,
                RefreshToken = internalToken.RefreshToken,
                Scope = internalToken.Scope,
                IdToken = internalToken.IdToken,
                RefreshExpiresIn = internalToken.RefreshExpiresIn
            });
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
}
