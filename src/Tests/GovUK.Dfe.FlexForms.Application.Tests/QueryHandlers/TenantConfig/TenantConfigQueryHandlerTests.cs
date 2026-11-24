using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.TenantConfig.Queries;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.TenantConfig;

/// <summary>The platform-facing tenant configuration and hostname lookups.</summary>
public class TenantConfigQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTime LoadedAt = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly ITenantPrincipalResolver _principals = Substitute.For<ITenantPrincipalResolver>();
    private readonly ITenantSettingsReader _settings = Substitute.For<ITenantSettingsReader>();
    private readonly ITenantHostnameResolver _hostnames = Substitute.For<ITenantHostnameResolver>();

    // ---- GetTenantConfiguration ----

    [Theory]
    [InlineData("http://schemas.microsoft.com/identity/claims/objectidentifier")]
    [InlineData("oid")]
    [InlineData("appid")]
    [InlineData("azp")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public async Task Configuration_RegisteredPrincipalGetsItsTenantsSettingsForTheTarget(string claimType)
    {
        _principals.ResolveAsync("principal-1", Arg.Any<CancellationToken>())
            .Returns(new TenantPrincipalResolution(TenantId, "Transfers", "Service"));
        var configuration = new Dictionary<string, string?> { ["Feature:On"] = "true" };
        _settings.GetConfigurationAsync(TenantId, "Api", Arg.Any<CancellationToken>())
            .Returns(new TenantConfigurationSnapshot(TenantId, "Transfers", LoadedAt, configuration));

        var result = await GetConfiguration(SignedIn(new Claim(claimType, "principal-1")), "Api");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantId, result.Value!.TenantId);
        Assert.Equal("Transfers", result.Value.TenantName);
        Assert.Equal("Api", result.Value.Target);
        Assert.Equal(LoadedAt, result.Value.LoadedAtUtc);
        Assert.Equal("true", result.Value.Configuration["Feature:On"]);
    }

    [Fact]
    public async Task Configuration_ObjectIdWinsOverAppId()
    {
        _principals.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TenantPrincipalResolution?)null);

        await GetConfiguration(SignedIn(new Claim("appid", "app"), new Claim("oid", "object")), "Api");

        await _principals.Received(1).ResolveAsync("object", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Configuration_SignedOutCallerIsForbidden()
    {
        var result = await GetConfiguration(new ClaimsPrincipal(new ClaimsIdentity()), "Api");

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _principals.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task Configuration_NoHttpContextIsForbidden()
    {
        var result = await GetConfiguration(user: null, "Api");

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Configuration_TokenWithoutAPrincipalIdIsForbidden()
    {
        var result = await GetConfiguration(SignedIn(new Claim(ClaimTypes.Email, "user@example.com")), "Api");

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _principals.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task Configuration_UnregisteredPrincipalIsForbidden()
    {
        _principals.ResolveAsync("stranger", Arg.Any<CancellationToken>()).Returns((TenantPrincipalResolution?)null);

        var result = await GetConfiguration(SignedIn(new Claim("oid", "stranger")), "Api");

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _settings.DidNotReceiveWithAnyArgs().GetConfigurationAsync(default, default!, default);
    }

    [Fact]
    public async Task Configuration_InactiveTenantIsNotFound()
    {
        _principals.ResolveAsync("principal-1", Arg.Any<CancellationToken>())
            .Returns(new TenantPrincipalResolution(TenantId, "Transfers", "Service"));
        _settings.GetConfigurationAsync(TenantId, "Web", Arg.Any<CancellationToken>()).Returns((TenantConfigurationSnapshot?)null);

        var result = await GetConfiguration(SignedIn(new Claim("oid", "principal-1")), "Web");

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    // ---- ResolveTenantByHostname ----

    [Fact]
    public async Task Hostname_MappedToAnActiveTenantResolves()
    {
        _hostnames.ResolveAsync("apply.example.gov.uk", Arg.Any<CancellationToken>())
            .Returns(new TenantHostnameResolution(TenantId, "Transfers", "apply.example.gov.uk"));

        var result = await ResolveHostname("apply.example.gov.uk");

        Assert.Equal(TenantId, result.Value!.TenantId);
        Assert.Equal("Transfers", result.Value.TenantName);
        Assert.Equal("apply.example.gov.uk", result.Value.Hostname);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Hostname_BlankFailsValidation(string hostname)
    {
        var result = await ResolveHostname(hostname);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        await _hostnames.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task Hostname_UnmappedIsNotFound()
    {
        _hostnames.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TenantHostnameResolution?)null);

        var result = await ResolveHostname(" unknown.example.gov.uk ");

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Contains("'unknown.example.gov.uk'", result.Error);
    }

    private Task<Result<TenantConfigurationDto>> GetConfiguration(
        ClaimsPrincipal? user,
        string target)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(user is null ? null : new DefaultHttpContext { User = user });
        return new GetTenantConfigurationQueryHandler(accessor, _principals, _settings)
            .Handle(new GetTenantConfigurationQuery(target), CancellationToken.None);
    }

    private Task<Result<TenantResolutionDto>> ResolveHostname(string hostname) =>
        new ResolveTenantByHostnameQueryHandler(_hostnames).Handle(new ResolveTenantByHostnameQuery(hostname), CancellationToken.None);

    private static ClaimsPrincipal SignedIn(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));
}
