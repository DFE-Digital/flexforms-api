using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

            var dbUser = await (new GetUserWithAllTemplatePermissionsQueryObject(email))
                .Apply(userRepo.Query().AsNoTracking())
                .Include(u => u.Role)
                .FirstOrDefaultAsync(cancellationToken: ct);

            if (dbUser is null)
                return Result<ExchangeTokenDto>.NotFound($"User not found for email {email}");

            if (dbUser.Role is null)
                return Result<ExchangeTokenDto>.Conflict($"User {email} has no role assigned");

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
                    tenantContextAccessor.CurrentTenant.Name,
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
            var currentTenant = tenantContextAccessor.CurrentTenant;
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

            // Add the user's role if it's not already there
            if (!identity.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == dbUser.Role.Name))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, dbUser.Role.Name));
            }

            // Merge Entra / app roles from the authenticated request principal, avoiding duplicates
            foreach (var svcRole in svcRoles)
            {
                var isExcludedRole =
                    (svcRole.Type == ClaimTypes.Role || svcRole.Type == "roles") &&
                    (svcRole.Value.Equals(RoleNames.Admin, StringComparison.OrdinalIgnoreCase) ||
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
    }
}
