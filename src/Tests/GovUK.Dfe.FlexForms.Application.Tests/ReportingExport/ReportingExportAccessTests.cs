using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.ReportingExport;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.ReportingExport;

public class ReportingExportAccessTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly ITenantTemplateResolver _templateResolver = Substitute.For<ITenantTemplateResolver>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly ReportingExportAccess _access;

    public ReportingExportAccessTests()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(new TenantConfiguration(TenantId, "Transfers", new ConfigurationBuilder().Build(), Array.Empty<string>()));
        _templateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>()).Returns(true);
        _access = new ReportingExportAccess(_permissionChecker, _tenantContext, _templateResolver, _httpContextAccessor);
    }

    [Fact]
    public async Task CheckAsync_ShouldAllow_TenantAdminOfOwnTenantAndTemplate()
    {
        Assert.Null(await _access.CheckAsync(TenantId, TemplateId, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_ShouldForbid_WhenCallerIsNotTenantAdmin()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(false);

        var denial = await _access.CheckAsync(TenantId, null, CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, denial?.Code);
    }

    [Fact]
    public async Task CheckAsync_ShouldForbid_WhenRouteTenantIsNotCurrentTenant()
    {
        var denial = await _access.CheckAsync(Guid.NewGuid(), null, CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, denial?.Code);
    }

    [Fact]
    public async Task CheckAsync_ShouldReturnNotFound_WhenTemplateBelongsToAnotherTenant()
    {
        _templateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateId), Arg.Any<CancellationToken>()).Returns(false);

        var denial = await _access.CheckAsync(TenantId, TemplateId, CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, denial?.Code);
    }

    [Fact]
    public async Task CheckAsync_ShouldNotLookUpTemplate_WhenNoneIsNamed()
    {
        Assert.Null(await _access.CheckAsync(TenantId, null, CancellationToken.None));

        await _templateResolver.DidNotReceiveWithAnyArgs().IsTemplateInCurrentTenantAsync(default!, default);
    }

    [Fact]
    public void ActingUser_ShouldPreferEmailClaim()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Ada"), new Claim(ClaimTypes.Email, "ada@example.gov.uk")], "test");
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });

        Assert.Equal("ada@example.gov.uk", _access.ActingUser);
    }

    [Fact]
    public void ActingUser_ShouldFallBackToUnknown_WithoutAUser()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        Assert.Equal("unknown", _access.ActingUser);
    }

    [Fact]
    public void Denial_ShouldMapToMatchingResult()
    {
        Assert.Equal(DomainErrorCode.NotFound, new ReportingExportDenial(DomainErrorCode.NotFound, "x").As<int>().ErrorCode);
        Assert.Equal(DomainErrorCode.Forbidden, new ReportingExportDenial(DomainErrorCode.Forbidden, "x").As<int>().ErrorCode);
    }
}
