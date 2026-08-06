using System.Security.Claims;
using AutoFixture.Xunit2;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using MockQueryable.NSubstitute;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.ClaimProviders;

public class TemplatePermissionsClaimProviderTests
{
    private static TemplatePermissionsClaimProvider CreateProvider(
        ILogger<TemplatePermissionsClaimProvider> logger,
        IEaRepository<User> userRepo) =>
        new(logger, userRepo, Substitute.For<IHttpContextAccessor>());

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnEmpty_WhenIssuerInvalid()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://example.com"),
            new Claim("appid", "cid")
        }));
        var logger = Substitute.For<ILogger<TemplatePermissionsClaimProvider>>();
        var userRepo = Substitute.For<IEaRepository<User>>();

        var provider = CreateProvider(logger, userRepo);

        var result = await provider.GetClaimsAsync(principal);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnEmpty_WhenAppIdMissing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc")
        }));
        var userRepo = Substitute.For<IEaRepository<User>>();

        var logger = Substitute.For<ILogger<TemplatePermissionsClaimProvider>>();
        var provider = CreateProvider(logger, userRepo);

        var result = await provider.GetClaimsAsync(principal);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnEmpty_WhenUserNotFound()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc"),
            new Claim("appid", "cid")
        }));
        var userRepo = Substitute.For<IEaRepository<User>>();

        userRepo.Query().Returns(Array.Empty<User>().AsQueryable().BuildMockDbSet());

        var logger = Substitute.For<ILogger<TemplatePermissionsClaimProvider>>();
        var provider = CreateProvider(logger, userRepo);

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
        userRepo.Query().Returns(new[] { user }.AsQueryable().BuildMockDbSet());

        var logger = Substitute.For<ILogger<TemplatePermissionsClaimProvider>>();
        var provider = CreateProvider(logger, userRepo);

        var result = await provider.GetClaimsAsync(principal);

        Assert.Empty(result);
    }

    [Theory, AutoData]
    public async Task GetClaimsAsync_ShouldReturnClaims_WhenPermissionsReturned(Guid templateId)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, "https://sts.windows.net/abc"),
            new Claim("appid", "cid")
        }));
        var userRepo = Substitute.For<IEaRepository<User>>();

        var userId = new UserId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var templatePermission = new Permission(
            new PermissionId(Guid.NewGuid()),
            userId,
            applicationId: null,
            templateId.ToString(),
            ResourceType.Template,
            AccessType.Read,
            DateTime.UtcNow,
            userId);

        var user = new User(
            id: userId,
            roleId: roleId,
            name: "Test User",
            email: "test@example.com",
            createdOn: DateTime.UtcNow,
            createdBy: null,
            lastModifiedOn: null,
            lastModifiedBy: null,
            externalProviderId: "cid",
            initialPermissions: [templatePermission]);
        userRepo.Query().Returns(new[] { user }.AsQueryable().BuildMockDbSet());

        var logger = Substitute.For<ILogger<TemplatePermissionsClaimProvider>>();
        var provider = CreateProvider(logger, userRepo);

        var result = (await provider.GetClaimsAsync(principal)).ToList();

        Assert.Single(result);
        Assert.Equal("permission", result[0].Type);
        Assert.Equal($"Template:{templateId}:Read", result[0].Value);
    }
}
