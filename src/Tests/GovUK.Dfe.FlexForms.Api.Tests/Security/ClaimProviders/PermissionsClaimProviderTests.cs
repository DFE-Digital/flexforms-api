using AutoFixture;
using AutoFixture.Xunit2;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using System.Security.Claims;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Xunit;
using MockQueryable.NSubstitute;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.ClaimProviders;

public class PermissionsClaimProviderTests
{
    private static PermissionsClaimProvider CreateProvider(
        ISender sender,
        ILogger<PermissionsClaimProvider> logger,
        IEaRepository<User> userRepo) =>
        new(sender, logger, userRepo, Substitute.For<IHttpContextAccessor>());

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnEmpty_WhenIssuerInvalid()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://example.com"),
            new Claim("appid", "cid")
        }));
        var sender = Substitute.For<ISender>();
        var logger = Substitute.For<ILogger<PermissionsClaimProvider>>();
        var userRepo = Substitute.For<IEaRepository<User>>();

        var provider = CreateProvider(sender, logger, userRepo);

        var result = await provider.GetClaimsAsync(principal);

        Assert.Empty(result);
        await sender.DidNotReceive().Send(Arg.Any<GetAllUserPermissionsQuery>());
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnEmpty_WhenAppIdMissing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc")
        }));
        var sender = Substitute.For<ISender>();
        var logger = Substitute.For<ILogger<PermissionsClaimProvider>>();
        var userRepo = Substitute.For<IEaRepository<User>>();

        var provider = CreateProvider(sender, logger, userRepo);

        var result = await provider.GetClaimsAsync(principal);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnEmpty_WhenQueryFails()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc"),
            new Claim("appid", "cid")
        }));
        var sender = Substitute.For<ISender>();
        var userRepo = Substitute.For<IEaRepository<User>>();

        var userId = new UserId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var user = new User(
            id: userId,
            roleId: roleId,
            name: "Test User",
            email: "test@example.com",
            createdOn: DateTime.UtcNow,
            createdBy: null,
            lastModifiedOn: null,
            lastModifiedBy: null,
            externalProviderId: "cid");
        ReturnsUsers(userRepo, user);

        sender.Send(Arg.Is<GetAllUserPermissionsQuery>(q => q.UserId == userId))
            .Returns(Task.FromResult(Result<UserAuthorizationDto>.Failure("err")));
        var logger = Substitute.For<ILogger<PermissionsClaimProvider>>();
        var provider = CreateProvider(sender, logger, userRepo);

        var result = await provider.GetClaimsAsync(principal);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnEmpty_WhenNoPermissions()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc"),
            new Claim("appid", "cid")
        }));
        var sender = Substitute.For<ISender>();
        var userRepo = Substitute.For<IEaRepository<User>>();

        var userId = new UserId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var user = new User(
            id: userId,
            roleId: roleId,
            name: "Test User",
            email: "test@example.com",
            createdOn: DateTime.UtcNow,
            createdBy: null,
            lastModifiedOn: null,
            lastModifiedBy: null,
            externalProviderId: "cid");
        ReturnsUsers(userRepo, user);

        var emptyAuth = new UserAuthorizationDto
        {
            Permissions = Array.Empty<UserPermissionDto>(),
            Roles = Array.Empty<string>()
        };
        sender.Send(Arg.Any<GetAllUserPermissionsQuery>())
            .Returns(Task.FromResult(Result<UserAuthorizationDto>.Success(emptyAuth)));
        var logger = Substitute.For<ILogger<PermissionsClaimProvider>>();
        var provider = CreateProvider(sender, logger, userRepo);

        var result = await provider.GetClaimsAsync(principal);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnEmpty_WhenUserHasNoRole()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc"),
            new Claim("appid", "cid")
        }));
        var sender = Substitute.For<ISender>();
        var userRepo = Substitute.For<IEaRepository<User>>();

        var userId = new UserId(Guid.NewGuid());
        var user = new User(
            id: userId,
            roleId: new RoleId(Guid.NewGuid()),
            name: "Test User",
            email: "test@example.com",
            createdOn: DateTime.UtcNow,
            createdBy: null,
            lastModifiedOn: null,
            lastModifiedBy: null,
            externalProviderId: "cid");

        ReturnsUsers(userRepo, user);

        var logger = Substitute.For<ILogger<PermissionsClaimProvider>>();
        var provider = CreateProvider(sender, logger, userRepo);

        var result = await provider.GetClaimsAsync(principal);

        Assert.Empty(result);
    }

    [Theory, AutoData]
    public async Task GetClaimsAsync_ShouldReturnClaims_WhenPermissionsReturned(string key)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc"),
            new Claim("appid", "cid")
        }));
        var sender = Substitute.For<ISender>();
        var userRepo = Substitute.For<IEaRepository<User>>();

        var userId = new UserId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var user = new User(
            id: userId,
            roleId: roleId,
            name: "Test User",
            email: "test@example.com",
            createdOn: DateTime.UtcNow,
            createdBy: null,
            lastModifiedOn: null,
            lastModifiedBy: null,
            externalProviderId: "cid");
        user.GetType().GetProperty("Role")!.SetValue(user, new Role(roleId, "TestRole"));
        ReturnsUsers(userRepo, user);

        var authDto = new UserAuthorizationDto
        {
            Permissions =
            [
                new UserPermissionDto { ResourceType = ResourceType.Application, ResourceKey = key, AccessType = AccessType.Read }
            ],
            Roles = ["TestRole"]
        };
        sender.Send(Arg.Is<GetAllUserPermissionsQuery>(q => q.UserId == userId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<UserAuthorizationDto>.Success(authDto)));
        var logger = Substitute.For<ILogger<PermissionsClaimProvider>>();
        var provider = CreateProvider(sender, logger, userRepo);

        var result = (await provider.GetClaimsAsync(principal)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Type == "permission" && c.Value == $"Application:{key}:Read");
        Assert.Contains(result, c => c.Type == ClaimTypes.Role && c.Value == "TestRole");
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldSkipSecondCall_WhenAlreadyEnrichedOnRequest()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc"),
            new Claim("appid", "cid")
        }));
        var sender = Substitute.For<ISender>();
        var userRepo = Substitute.For<IEaRepository<User>>();
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var userId = new UserId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var user = new User(
            id: userId,
            roleId: roleId,
            name: "Test User",
            email: "test@example.com",
            createdOn: DateTime.UtcNow,
            createdBy: null,
            lastModifiedOn: null,
            lastModifiedBy: null,
            externalProviderId: "cid");
        user.GetType().GetProperty("Role")!.SetValue(user, new Role(roleId, "TestRole"));
        ReturnsUsers(userRepo, user);

        sender.Send(Arg.Any<GetAllUserPermissionsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<UserAuthorizationDto>.Success(new UserAuthorizationDto
            {
                Permissions = Array.Empty<UserPermissionDto>(),
                Roles = Array.Empty<string>()
            })));

        var provider = new PermissionsClaimProvider(
            sender,
            Substitute.For<ILogger<PermissionsClaimProvider>>(),
            userRepo,
            httpContextAccessor);

        _ = await provider.GetClaimsAsync(principal);
        _ = await provider.GetClaimsAsync(principal);

        await sender.Received(1).Send(Arg.Any<GetAllUserPermissionsQuery>(), Arg.Any<CancellationToken>());
    }

    private static void ReturnsUsers(IEaRepository<User> userRepo, params User[] users)
    {
        var dbSet = users.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(dbSet);
    }
}
