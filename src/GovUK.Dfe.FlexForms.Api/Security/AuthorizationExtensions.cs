using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Security;
using GovUK.Dfe.CoreLibs.Security.Authorization;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace GovUK.Dfe.FlexForms.Api.Security
{
    [ExcludeFromCodeCoverage]
    public static class AuthorizationExtensions
    {
        public static IServiceCollection AddCustomAuthorization(
            this IServiceCollection services,
            IConfiguration configuration,
            ITenantConfigurationProvider tenantConfigurationProvider)
        {
            // Collect all DfESignIn configurations from all tenants for multi-provider support
            var allTenants = tenantConfigurationProvider.GetAllTenants();

            // baseConfig is only used by CoreLibs APIs (ExternalIdentityValidator and the
            // permissions handlers) that expect a root IConfiguration; they don't carry any
            // per-tenant signing material. Per-tenant JWT settings live in the registry now.
            var baseConfig = configuration;
            
            // Register external identity validation with multi-provider support.
            // Initial list is from tenants loaded at startup; TenantExternalIdentityProviderReloader
            // rebuilds the validator when ITenantConfigurationChangedNotifier fires (no recycle).
            services.AddExternalIdentityValidation(baseConfig, multiOpts =>
            {
                foreach (var provider in TenantOidcProviderBuilder.BuildProviders(allTenants))
                {
                    multiOpts.Providers.Add(provider);
                }
            });

            services.AddSingleton<TenantExternalIdentityProviderReloader>();
            services.AddHostedService(sp => sp.GetRequiredService<TenantExternalIdentityProviderReloader>());

            // SaaS: named TokenSettings are resolved live from ITenantConfigurationProvider so
            // /tokens/exchange signs with the same Authorization:TokenSettings secret that
            // TenantBearer validates. Must register as IConfigureOptions (OptionsFactory DI),
            // not only IConfigureNamedOptions — otherwise Configure is never called and SecretKey
            // stays empty (IDX10703).
            services.AddSingleton<TenantTokenSettingsConfigurator>();
            services.AddSingleton<IConfigureOptions<TokenSettings>>(sp =>
                sp.GetRequiredService<TenantTokenSettingsConfigurator>());
            services.AddSingleton<IConfigureNamedOptions<TokenSettings>>(sp =>
                sp.GetRequiredService<TenantTokenSettingsConfigurator>());
            services.AddSingleton<IOptionsChangeTokenSource<TokenSettings>, TenantTokenSettingsChangeTokenSource>();
            services.AddUserTokenServiceFactory();
            services.AddHttpContextAccessor();

            var authBuilder = services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = AuthConstants.CompositeScheme;
                    options.DefaultChallengeScheme = AuthConstants.CompositeScheme;
                })
                .AddPolicyScheme(AuthConstants.CompositeScheme, "CompositeAuth", options =>
                {
                    // SaaS dispatcher: no more issuer sniffing. Bearer tokens (user JWTs and Entra
                    // service tokens) all go through the single dynamic TenantBearer scheme which
                    // looks the provider up in ITenantAuthProviderRegistry. API-key / mTLS dispatch
                    options.ForwardDefaultSelector = context =>
                    {
                        if (context.Request.Headers.ContainsKey(AuthConstants.ApiKeyHeader))
                            return AuthConstants.ApiKey;
                        if (context.Connection.ClientCertificate is not null)
                            return AuthConstants.Mtls;
                        return AuthConstants.TenantBearer;
                    };
                })
                .AddJwtBearer(AuthConstants.TenantBearer, _ =>
                {
                    // Configured via AddOptions<JwtBearerOptions> below so we can DI-inject
                    // ITenantAuthProviderRegistry without spinning a temporary service provider.
                })
                .AddScheme<Schemes.ApiKeyAuthenticationOptions, Schemes.ApiKeyAuthenticationHandler>(
                    AuthConstants.ApiKey, _ => { /* HeaderName defaults to X-Api-Key */ })
                .AddCertificate(AuthConstants.Mtls, certOpts =>
                {
                    // SaaS mTLS: accept any chain-built client cert and resolve the matching
                    // TenantAuthProvider by thumbprint. Issuer/CA pinning is delegated to the
                    // platform (App Gateway / Front Door) - we only verify identity here.
                    certOpts.AllowedCertificateTypes = Microsoft.AspNetCore.Authentication.Certificate.CertificateTypes.All;
                    certOpts.Events = new Microsoft.AspNetCore.Authentication.Certificate.CertificateAuthenticationEvents
                    {
                        OnCertificateValidated = OnCertificateValidated
                    };
                });

            var platformAzureAdSection = configuration.GetSection(PlatformConstants.AzureAdSection);
            if (!string.IsNullOrWhiteSpace(platformAzureAdSection["ClientId"]))
            {
                authBuilder.AddMicrosoftIdentityWebApi(
                    platformAzureAdSection,
                    jwtBearerScheme: AuthConstants.PlatformBearer);
            }

            services.AddOptions<JwtBearerOptions>(AuthConstants.TenantBearer)
                .Configure<IServiceProvider>(static (opts, sp) => ConfigureTenantBearer(
                    opts,
                    sp.GetRequiredService<ITenantAuthProviderRegistry>(),
                    sp.GetRequiredService<ITenantSigningKeyResolver>(),
                    sp.GetRequiredService<IHttpContextAccessor>()));

            // Infrastructure-side signing-key resolver: keeps JwtBearer wiring decoupled from the
            // pure-data Domain registry. Singleton because it owns long-lived OIDC ConfigurationManagers.
            services.AddSingleton<ITenantSigningKeyResolver, TenantSigningKeyResolver>();

            // SaaS: normalise the principal across TenantBearer / ApiKey / Mtls so downstream
            // policies and handlers can read tenant_id / is_service / email / roles uniformly
            // without branching on the active scheme.
            services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, TenantClaimsTransformation>();


            // set-up and define User and Template Permission policies
            var policyCustomizations = new Dictionary<string, Action<AuthorizationPolicyBuilder>>
            {
                ["CanReadTemplate"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.TemplatePermissionRequirement(AccessType.Read.ToString()));
                },
                ["CanWriteTemplate"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.TemplatePermissionRequirement(AccessType.Write.ToString()));
                },
                ["CanReadUser"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.UserPermissionRequirement(AccessType.Read.ToString()));
                },
                ["CanReadApplication"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.ApplicationPermissionRequirement(AccessType.Read.ToString()));
                },
                ["CanUpdateApplication"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.ApplicationPermissionRequirement(AccessType.Write.ToString()));
                },
                ["CanDeleteApplication"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.ApplicationPermissionRequirement(AccessType.Delete.ToString()));
                },
                ["CanReadAnyApplication"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.ApplicationListPermissionRequirement(AccessType.Read.ToString()));
                },
                ["CanCreateAnyApplication"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.AnyTemplatePermissionRequirement(AccessType.Write.ToString()));
                },
                ["CanListTemplates"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.AnyTemplatePermissionRequirement(AccessType.Read.ToString()));
                },
                ["CanCreateTemplate"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.TemplateManagePermissionRequirement());
                },
                ["CanManageUsers"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.UserManagePermissionRequirement());
                },
                ["CanReadApplicationFiles"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.ApplicationFilesPermissionRequirement(AccessType.Read.ToString()));
                },
                ["CanWriteApplicationFiles"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.ApplicationFilesPermissionRequirement(AccessType.Write.ToString()));
                },
                ["CanDeleteApplicationFiles"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.ApplicationFilesPermissionRequirement(AccessType.Delete.ToString()));
                },
                ["CanReadNotifications"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.NotificationsPermissionRequirement(AccessType.Read.ToString()));
                },
                ["CanWriteNotifications"] = pb =>
                {
                    pb.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
                    pb.RequireAuthenticatedUser();
                    pb.AddRequirements(new Handlers.NotificationsPermissionRequirement(AccessType.Write.ToString()));
                }
            };

            // Cookie authentication for SignalR
            services.AddAuthentication() // do NOT set defaults here
                .AddCookie("HubCookie", o =>
                {
                    o.Cookie.Name = "hubauth";
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    o.Cookie.SameSite = SameSiteMode.None;
                    o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                    o.SlidingExpiration = false;             // SignalR/WebSockets won't slide; we'll renew explicitly
                });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Cookies.CanReadNotifications", p =>
                        p.AddAuthenticationSchemes("HubCookie")
                            .RequireAuthenticatedUser()
                );

                // SaaS: provider-agnostic service-callers policy. Replaces SvcCanReadWrite. Any
                // TenantAuthProvider with IsServicePrincipal == true satisfies this policy, so
                // Entra apps, API-key callers and mTLS callers all pass the same gate.
                options.AddPolicy("ServiceCallers", p =>
                        p.AddAuthenticationSchemes(AuthConstants.CompositeScheme)
                            .RequireAuthenticatedUser()
                            .AddRequirements(new Handlers.ServicePrincipalRequirement()));

                // Interactive tenant Admin only (user JWT). Rejects client-credentials / API key / mTLS.
                options.AddPolicy(AuthConstants.TenantAdminUserPolicy, p =>
                        p.AddAuthenticationSchemes(AuthConstants.CompositeScheme)
                            .RequireAuthenticatedUser()
                            .AddRequirements(new Handlers.TenantAdminUserRequirement()));

                options.AddPolicy(PlatformConstants.PlatformHostPolicy, p =>
                    p.AddAuthenticationSchemes(AuthConstants.PlatformBearer)
                        .RequireAuthenticatedUser()
                        .AddRequirements(new Handlers.PlatformHostRoleRequirement()));

                options.AddPolicy(PlatformConstants.PlatformTenantConfigPolicy, p =>
                    p.AddAuthenticationSchemes(AuthConstants.PlatformBearer)
                        .RequireAuthenticatedUser()
                        .AddRequirements(new Handlers.PlatformTenantConfigRoleRequirement()));
            });

            services.AddApplicationAuthorization(
                baseConfig,
                policyCustomizations: policyCustomizations,
                apiAuthenticationScheme: AuthConstants.CompositeScheme,
                configureResourcePolicies: null);

            services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationFailureResponseHandler>();
            services.AddSingleton<IAuthorizationHandler, TemplatePermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, UserPermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, ApplicationPermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, ApplicationListPermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, AnyTemplatePermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, TemplateManagePermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, UserManagePermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, ApplicationFilesPermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, NotificationsPermissionHandler>();
            services.AddSingleton<IAuthorizationHandler, ServicePrincipalHandler>();
            services.AddSingleton<IAuthorizationHandler, Handlers.TenantAdminUserAuthorizationHandler>();
            services.AddSingleton<IAuthorizationHandler, Handlers.PlatformHostRoleAuthorizationHandler>();
            services.AddSingleton<IAuthorizationHandler, Handlers.PlatformTenantConfigRoleAuthorizationHandler>();
            services.AddTransient<ICustomClaimProvider, PermissionsClaimProvider>();
            services.AddTransient<ICustomClaimProvider, TemplatePermissionsClaimProvider>();
            services.AddTransient<ICustomClaimProvider, UserPermissionClaimProvider>();

            return services;
        }

        /// <summary>
        /// Configures the single dynamic <c>TenantBearer</c> JwtBearer scheme. All issuer/audience/
        /// signing-key validation is delegated to <paramref name="registry"/> at request time, so
        /// adding a new tenant or rotating a key requires no service restart - the registry is
        /// rebuilt by the configuration-change pub/sub.
        /// <para>
        /// Token-level <c>tenant_id</c> consistency is enforced in <see cref="EnforceTenantConsistencyAsync"/>:
        /// a token issued for tenant A cannot be replayed against tenant B even if A's signing key
        /// happens to be valid.
        /// </para>
        /// <para>
        /// <paramref name="httpContextAccessor"/> is used so audience validation can resolve scoped
        /// <see cref="ITenantContextAccessor"/> from <see cref="HttpContext.RequestServices"/> per request.
        /// JwtBearer options configuration runs against the root provider and cannot inject scoped services directly.
        /// </para>
        /// </summary>
        private static void ConfigureTenantBearer(
            JwtBearerOptions opts,
            ITenantAuthProviderRegistry registry,
            ITenantSigningKeyResolver signingKeyResolver,
            IHttpContextAccessor httpContextAccessor)
        {
            // IdentityModel 8+ defaults to JsonWebTokenHandler, which reports misleading
            // IDX10517 ("kid is missing") for HS256 internal JWTs that omit kid (as issued by
            // UserTokenService). Align with the issuing handler for HMAC user tokens.
            opts.TokenHandlers.Clear();
            opts.TokenHandlers.Add(new JwtSecurityTokenHandler());

            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                TryAllIssuerSigningKeys = true,
                IssuerValidator = (issuer, _, _) =>
                    registry.HasAnyProviderForIssuer(issuer)
                        ? issuer
                        : throw new SecurityTokenInvalidIssuerException(issuer),
                IssuerSigningKeyResolver = (_, securityToken, _, _) =>
                {
                    var issuer = securityToken?.Issuer;
                    if (string.IsNullOrEmpty(issuer)) return Array.Empty<SecurityKey>();
                    return signingKeyResolver.GetSigningKeysAsync(issuer, CancellationToken.None).GetAwaiter().GetResult();
                },
                AudienceValidator = (audiences, securityToken, _) =>
                {
                    var issuer = securityToken?.Issuer;
                    if (string.IsNullOrEmpty(issuer))
                    {
                        return false;
                    }

                    var jwt = securityToken as JwtSecurityToken;
                    var azpOrAppId = PickAzpOrAppId(jwt);
                    var tenantAccessor = ResolveTenantContextAccessor(httpContextAccessor);
                    var tenantId = tenantAccessor?.CurrentTenant?.Id;
                    if (tenantId is null)
                    {
                        return registry.IsJwtAudienceValidForIssuerAnyTenant(issuer, audiences);
                    }

                    return registry.IsJwtAudienceValidForTenant(issuer, audiences, tenantId.Value, azpOrAppId);
                }
            };

            opts.Events = new JwtBearerEvents
            {
                OnTokenValidated = EnforceTenantConsistencyAsync,
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
                    logger?.LogWarning(context.Exception, "TenantBearer authentication failed");
                    return Task.CompletedTask;
                }
            };
        }

        /// <summary>
        /// Resolves scoped <see cref="ITenantContextAccessor"/> for the current HTTP request.
        /// </summary>
        private static ITenantContextAccessor? ResolveTenantContextAccessor(IHttpContextAccessor httpContextAccessor)
            => httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContextAccessor>();

        /// <summary>
        /// Enforces that a successfully-validated bearer token's <c>tenant_id</c> claim matches
        /// the request's resolved tenant (from <see cref="ITenantContextAccessor"/>). For Entra
        /// service tokens that don't carry <c>tenant_id</c>, the matched provider is resolved via
        /// <see cref="ITenantAuthProviderRegistry.ResolveJwtBearerProvider"/> (issuer + aud +
        /// <c>azp</c>/<c>appid</c> + current tenant) so many SaaS tenants can share one Entra directory.
        /// </summary>
        private static Task EnforceTenantConsistencyAsync(TokenValidatedContext context)
        {
            var tenantAccessor = context.HttpContext.RequestServices.GetService<ITenantContextAccessor>();
            var currentTenant = tenantAccessor?.CurrentTenant;

            // No tenant resolved (e.g. health/swagger endpoints) - allow.
            if (currentTenant is null) return Task.CompletedTask;

            var registry = context.HttpContext.RequestServices.GetService<ITenantAuthProviderRegistry>();
            var issuer = context.SecurityToken?.Issuer;
            var jwt = context.SecurityToken as JwtSecurityToken;
            var tokenAudiences = jwt?.Audiences ?? ExtractAudiencesFromPrincipal(context.Principal);
            var azpOrAppId = PickAzpOrAppId(jwt) ?? PickAzpOrAppIdFromPrincipal(context.Principal);

            TenantAuthProvider? matchedProvider = null;
            if (!string.IsNullOrEmpty(issuer) && registry is not null)
            {
                matchedProvider = registry.ResolveJwtBearerProvider(
                    issuer,
                    tokenAudiences,
                    currentTenant.Id,
                    azpOrAppId);
            }

            // Stash the matched provider for the ServicePrincipalRequirement handler.
            if (matchedProvider is not null)
            {
                TenantAuthPrincipalFactory.StashProvider(context.HttpContext, matchedProvider);
            }

            var tokenTenantClaim = context.Principal?.Claims
                .FirstOrDefault(c => c.Type == TenantAuthClaimTypes.TenantId)?.Value;

            if (!string.IsNullOrEmpty(tokenTenantClaim))
            {
                if (!string.Equals(tokenTenantClaim, currentTenant.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail($"Token tenant_id '{tokenTenantClaim}' does not match the resolved tenant '{currentTenant.Name}' ({currentTenant.Id}).");
                }
                return Task.CompletedTask;
            }

            if (matchedProvider is null && !string.IsNullOrEmpty(issuer) && registry?.HasAnyProviderForIssuer(issuer) == true)
            {
                context.Fail(
                    $"No unique auth provider for issuer '{issuer}' and tenant '{currentTenant.Name}' ({currentTenant.Id}). " +
                    "Ensure token aud matches configured audiences and azp/appid matches AzureAd:ClientId (or AuthProviders ClientId) for this tenant.");
                return Task.CompletedTask;
            }

            if (matchedProvider is null)
            {
                // Legacy/unknown issuer - log warning. Hard-fail once we are confident no legacy
                // tokens remain in circulation.
                var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
                logger?.LogWarning(
                    "TenantBearer token issuer '{Issuer}' has no matching provider in the registry. " +
                    "Tenant {TenantId} ({TenantName}). This grace path will be removed in a future release.",
                    issuer, currentTenant.Id, currentTenant.Name);
            }

            return Task.CompletedTask;
        }

        /// <summary>Reads <c>aud</c> from the validated principal when the security token is not a <see cref="JwtSecurityToken"/>.</summary>
        private static IEnumerable<string> ExtractAudiencesFromPrincipal(ClaimsPrincipal? principal)
        {
            if (principal is null) yield break;
            foreach (var c in principal.FindAll(JwtRegisteredClaimNames.Aud))
            {
                if (!string.IsNullOrEmpty(c.Value))
                {
                    yield return c.Value;
                }
            }
        }

        /// <summary>Resolves Entra <c>azp</c> / <c>appid</c> from a JWT for per-app registration matching.</summary>
        private static string? PickAzpOrAppId(JwtSecurityToken? jwt)
        {
            if (jwt is null) return null;
            var azp = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Azp)?.Value;
            if (!string.IsNullOrEmpty(azp)) return azp;
            var appid = jwt.Claims.FirstOrDefault(c => c.Type == "appid")?.Value;
            if (!string.IsNullOrEmpty(appid)) return appid;
            return jwt.Claims.FirstOrDefault(c =>
                    c.Type == "http://schemas.microsoft.com/identity/claims/appid")
                ?.Value;
        }

        private static string? PickAzpOrAppIdFromPrincipal(ClaimsPrincipal? principal)
        {
            if (principal is null) return null;
            var azp = principal.FindFirst(JwtRegisteredClaimNames.Azp)?.Value;
            if (!string.IsNullOrEmpty(azp)) return azp;
            var appid = principal.FindFirst("appid")?.Value;
            if (!string.IsNullOrEmpty(appid)) return appid;
            return principal.FindFirst("http://schemas.microsoft.com/identity/claims/appid")?.Value;
        }

        /// <summary>
        /// Handles a validated client certificate: looks up the matching <see cref="TenantAuthProvider"/>
        /// by thumbprint, fails the context if there's no match, otherwise replaces the principal
        /// with a uniform tenant principal built by <see cref="TenantAuthPrincipalFactory"/>.
        /// </summary>
        private static Task OnCertificateValidated(Microsoft.AspNetCore.Authentication.Certificate.CertificateValidatedContext ctx)
        {
            var registry = ctx.HttpContext.RequestServices.GetRequiredService<ITenantAuthProviderRegistry>();
            var provider = registry.GetByCertificateThumbprint(ctx.ClientCertificate.Thumbprint);
            if (provider is null)
            {
                ctx.Fail("Unknown client certificate.");
                return Task.CompletedTask;
            }

            TenantAuthPrincipalFactory.StashProvider(ctx.HttpContext, provider);
            // Preserve any claims the certificate handler already projected (subject, thumbprint, etc.)
            var existingClaims = ctx.Principal?.Claims ?? Enumerable.Empty<System.Security.Claims.Claim>();
            ctx.Principal = TenantAuthPrincipalFactory.BuildPrincipal(provider, AuthConstants.Mtls, existingClaims);
            ctx.Success();
            return Task.CompletedTask;
        }
    }
}
