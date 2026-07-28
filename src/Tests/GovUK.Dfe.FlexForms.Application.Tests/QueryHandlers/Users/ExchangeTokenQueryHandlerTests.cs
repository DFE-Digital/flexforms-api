using AutoFixture;
using AutoFixture.Xunit2;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.CoreLibs.Security.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MockQueryable;
using MockQueryable.NSubstitute;
using NSubstitute;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Users;

public class ExchangeTokenQueryHandlerTests
{
    private static TenantConfiguration CreateTenant()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        return new TenantConfiguration(
            Guid.NewGuid(),
            "TestTenant",
            configuration,
            Array.Empty<string>());
    }

    private static IUserAccessibleTemplateService CreateAccessibleService(params TemplateId[] accessible)
    {
        var service = Substitute.For<IUserAccessibleTemplateService>();
        service.GetAccessibleTemplateIdsAsync(
                Arg.Any<IEnumerable<TemplatePermission>>(),
                Arg.Any<CancellationToken>())
            .Returns(accessible.ToList().AsReadOnly());
        return service;
    }

    private static ITenantMembershipService CreateMembershipService(User user, string roleName = "TestRole")
    {
        var role = new Role(new RoleId(Guid.NewGuid()), roleName, tenantId: Guid.NewGuid(), isSystem: true);
        var membership = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            Guid.NewGuid(),
            user.Id!,
            role.Id!,
            DateTime.UtcNow);
        membership.GetType().GetProperty(nameof(TenantMembership.Role))!.SetValue(membership, role);

        var service = Substitute.For<ITenantMembershipService>();
        service.GetActiveMembershipAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(membership);
        return service;
    }

    private static ITenantOidcAudienceBinder CreateAudienceBinder(bool matches = true)
    {
        var binder = Substitute.For<ITenantOidcAudienceBinder>();
        binder.TokenMatchesTenant(Arg.Any<TenantConfiguration>(), Arg.Any<IEnumerable<string>>())
            .Returns(matches);
        return binder;
    }

    private static ExchangeTokenQueryHandler CreateHandler(
        IExternalIdentityValidator externalValidator,
        IEaRepository<User> userRepo,
        IUserTokenServiceFactory tokenServiceFactory,
        IHttpContextAccessor httpContextAccessor,
        ITenantContextAccessor tenantContextAccessor,
        ICustomRequestChecker internalRequestChecker,
        ILogger<ExchangeTokenQueryHandler> logger,
        IUserAccessibleTemplateService? accessibleTemplateService = null,
        ITenantMembershipService? membershipService = null,
        ITenantOidcAudienceBinder? audienceBinder = null)
    {
        return new ExchangeTokenQueryHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            accessibleTemplateService ?? CreateAccessibleService(new TemplateId(Guid.NewGuid())),
            membershipService ?? Substitute.For<ITenantMembershipService>(),
            audienceBinder ?? CreateAudienceBinder(),
            internalRequestChecker,
            logger);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ValidToken_ShouldReturnExchangeTokenDto(
        string subjectToken,
        string email,
        UserCustomization userCustom,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        var tenant = CreateTenant();
        tenantContextAccessor.CurrentTenant.Returns(tenant);
        var accessibleTemplateId = new TemplateId(Guid.NewGuid());

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) })));

        userCustom.OverrideEmail = email;
        userCustom.OverrideTemplatePermissions = new[]
        {
            new TemplatePermission(
                new TemplatePermissionId(Guid.NewGuid()),
                new UserId(Guid.NewGuid()),
                accessibleTemplateId,
                AccessType.Read,
                DateTime.UtcNow,
                new UserId(Guid.NewGuid()))
        };
        var user = new Fixture().Customize(userCustom).Create<User>();
        user.GetType().GetProperty("Role")!.SetValue(user, new Role(user.RoleId, "TestRole"));

        var userQueryable = new List<User> { user }.AsQueryable().BuildMock();
        userRepo.Query().Returns(userQueryable);

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, "TestRole") },
            authenticationType: "Bearer")));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var expectedInternalToken = new Token
        {
            AccessToken = "internal-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };
        var tokenService = Substitute.For<IUserTokenService>();
        tokenServiceFactory.GetService(tenant.Id.ToString()).Returns(tokenService);
        tokenService.GetUserTokenModelAsync(Arg.Any<ClaimsPrincipal>())
            .Returns(Task.FromResult(expectedInternalToken));

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            internalRequestChecker,
            logger,
            CreateAccessibleService(accessibleTemplateId),
            CreateMembershipService(user, "TestRole"));

        var result = await handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        await tokenService.Received(1).GetUserTokenModelAsync(Arg.Is<ClaimsPrincipal>(p =>
            p.HasClaim(ClaimTypes.Role, "TestRole") &&
            p.HasClaim(TenantAuthClaimTypes.TenantId, tenant.Id.ToString())));
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_UserWithAccessToOneOfManyTenantTemplates_ShouldSucceed(
        string subjectToken,
        string email,
        UserCustomization userCustom,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        var tenant = CreateTenant();
        tenantContextAccessor.CurrentTenant.Returns(tenant);

        var permittedTemplateId = new TemplateId(Guid.NewGuid());
        var otherTenantTemplateId = new TemplateId(Guid.NewGuid());

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) })));

        userCustom.OverrideEmail = email;
        userCustom.OverrideTemplatePermissions = new[]
        {
            new TemplatePermission(
                new TemplatePermissionId(Guid.NewGuid()),
                new UserId(Guid.NewGuid()),
                permittedTemplateId,
                AccessType.Read,
                DateTime.UtcNow,
                new UserId(Guid.NewGuid()))
        };
        var user = new Fixture().Customize(userCustom).Create<User>();
        user.GetType().GetProperty("Role")!.SetValue(user, new Role(user.RoleId, "TestRole"));
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Bearer")));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var tokenService = Substitute.For<IUserTokenService>();
        tokenServiceFactory.GetService(tenant.Id.ToString()).Returns(tokenService);
        tokenService.GetUserTokenModelAsync(Arg.Any<ClaimsPrincipal>())
            .Returns(Task.FromResult(new Token { AccessToken = "token", ExpiresIn = 60 }));

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            internalRequestChecker,
            logger,
            CreateAccessibleService(permittedTemplateId, otherTenantTemplateId),
            CreateMembershipService(user));

        var result = await handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_NoTenantMembership_PlatformSuperAdmin_ShouldSucceed(
        string subjectToken,
        string email,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        var tenant = CreateTenant();
        tenantContextAccessor.CurrentTenant.Returns(tenant);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) })));

        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.AdminRoleId),
            "platform admin",
            email,
            DateTime.UtcNow,
            null,
            null,
            null);
        user.GetType().GetProperty("Role")!.SetValue(
            user,
            new Role(new RoleId(RoleConstants.AdminRoleId), RoleNames.SuperAdmin));
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        var membershipService = Substitute.For<ITenantMembershipService>();
        membershipService.GetActiveMembershipAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns((TenantMembership?)null);

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Bearer")));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var tokenService = Substitute.For<IUserTokenService>();
        tokenServiceFactory.GetService(tenant.Id.ToString()).Returns(tokenService);
        tokenService.GetUserTokenModelAsync(Arg.Any<ClaimsPrincipal>())
            .Returns(Task.FromResult(new Token { AccessToken = "sa-token", ExpiresIn = 60 }));

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            internalRequestChecker,
            logger,
            membershipService: membershipService);

        var result = await handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await tokenService.Received(1).GetUserTokenModelAsync(Arg.Is<ClaimsPrincipal>(p =>
            p.HasClaim(ClaimTypes.Role, RoleNames.SuperAdmin)));
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_NoTenantMembership_ShouldForbid(
        string subjectToken,
        string email,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        var tenant = CreateTenant();
        tenantContextAccessor.CurrentTenant.Returns(tenant);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) })));

        var user = new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "test user", email, DateTime.UtcNow,
            null, null, null);
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        var membershipService = Substitute.For<ITenantMembershipService>();
        membershipService.GetActiveMembershipAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns((TenantMembership?)null);

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            internalRequestChecker,
            logger,
            membershipService: membershipService);

        var result = await handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not a member", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_AudienceMismatch_ShouldForbid(
        string subjectToken,
        string email,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DfESignIn:ClientId"] = "transfers-client"
            })
            .Build();
        var tenant = new TenantConfiguration(Guid.NewGuid(), "Transfers", configuration, Array.Empty<string>());
        tenantContextAccessor.CurrentTenant.Returns(tenant);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) })));

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            internalRequestChecker,
            logger,
            audienceBinder: CreateAudienceBinder(matches: false));

        var result = await handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not issued for this tenant", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_MissingEmail_ShouldReturnFailure(
        string subjectToken,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>())));

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            internalRequestChecker,
            logger);

        var result = await handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Missing email", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_UserNotFound_ShouldReturnNotFound(
        string subjectToken,
        string email,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        tenantContextAccessor.CurrentTenant.Returns(CreateTenant());

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) })));

        userRepo.Query().Returns(new List<User>().AsQueryable().BuildMock());

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            internalRequestChecker,
            logger);

        var result = await handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal($"User not found for email {email}", result.Error);
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_InvalidToken_ShouldPropagateException(
        string subjectToken,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpCtxAcc,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        var faultedTask = Task.FromException<ClaimsPrincipal>(new SecurityTokenException("Invalid token"));
        externalValidator.ValidateIdTokenAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(faultedTask);

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpCtxAcc,
            tenantContextAccessor,
            internalRequestChecker,
            logger);

        var ex = await Assert.ThrowsAsync<SecurityTokenException>(
            () => handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None));
        Assert.Equal("Invalid token", ex.Message);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_UserWithoutAccessibleTemplate_AllowsLogin(
        string subjectToken,
        string email,
        UserCustomization userCustom,
        [Frozen] IExternalIdentityValidator externalValidator,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IUserTokenServiceFactory tokenServiceFactory,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] ITenantContextAccessor tenantContextAccessor,
        [Frozen][FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker,
        [Frozen] ILogger<ExchangeTokenQueryHandler> logger)
    {
        var tenant = CreateTenant();
        tenantContextAccessor.CurrentTenant.Returns(tenant);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) })));

        userCustom.OverrideEmail = email;
        userCustom.OverrideTemplatePermissions = Array.Empty<TemplatePermission>();
        var user = new Fixture().Customize(userCustom).Create<User>();
        user.GetType().GetProperty("Role")!.SetValue(user, new Role(user.RoleId, "TestRole"));
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity()));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var expectedInternalToken = new Token
        {
            AccessToken = "internal-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };
        var tokenService = Substitute.For<IUserTokenService>();
        tokenServiceFactory.GetService(tenant.Id.ToString()).Returns(tokenService);
        tokenService.GetUserTokenModelAsync(Arg.Any<ClaimsPrincipal>())
            .Returns(Task.FromResult(expectedInternalToken));

        var handler = CreateHandler(
            externalValidator,
            userRepo,
            tokenServiceFactory,
            httpContextAccessor,
            tenantContextAccessor,
            internalRequestChecker,
            logger,
            CreateAccessibleService(),
            CreateMembershipService(user));

        var result = await handler.Handle(new ExchangeTokenQuery(subjectToken), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("internal-token", result.Value!.AccessToken);
    }
}
