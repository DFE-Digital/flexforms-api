using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using NSubstitute;
using static GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin.TenantAdminTestData;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.TenantAdmin;

public class GetSafeTenantSettingsQueryHandlerTests
{
    private readonly ITenantSettingsQuery _settings = Substitute.For<ITenantSettingsQuery>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly GetSafeTenantSettingsQueryHandler _handler;

    public GetSafeTenantSettingsQueryHandlerTests()
    {
        _tenantContext.CurrentTenant.Returns(Tenant());
        _handler = new GetSafeTenantSettingsQueryHandler(_settings, _tenantContext, _permissions);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InteractiveAdminOrSuperAdmin_SeesOnlyNonSecretDelegatedSettings(bool tenantAdmin, bool superAdmin)
    {
        _permissions.IsInteractiveTenantAdmin().Returns(tenantAdmin);
        _permissions.IsInteractivePlatformAdmin().Returns(superAdmin);
        _settings.ListSettingsAsync(OwnTenantId, Arg.Any<CancellationToken>()).Returns(new TenantSettingsList(
            OwnTenantId,
            "Transfers",
            [
                Row("Dashboard", "Web", """{"EnableFilters":true}"""),
                Row("FileValidation", "Shared", """{"Mode":"Strict"}"""),
                Row("NotificationBanner", "Web", """{"Text":"x"}""", isSecret: true),
                Row("Layout", "Web", """{"Theme":"govuk"}"""),
                Row("ConnectionStrings", "Shared", """{"Db":"secret"}""", isSecret: true),
                Row("EntraSso", "Web", """{"Enabled":true}""")
            ]));

        var result = await _handler.Handle(new GetSafeTenantSettingsQuery(OwnTenantId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Transfers", result.Value!.TenantName);
        Assert.Equal(["Dashboard", "FileValidation"], result.Value.Settings.Select(s => s.Category).Order());
    }

    [Fact]
    public async Task NonAdminOrServicePrincipal_IsForbidden()
    {
        var result = await _handler.Handle(new GetSafeTenantSettingsQuery(OwnTenantId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _settings.DidNotReceiveWithAnyArgs().ListSettingsAsync(default, default);
    }

    [Fact]
    public async Task NoTenantContext_IsForbidden()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await _handler.Handle(new GetSafeTenantSettingsQuery(OwnTenantId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task AnotherTenant_IsForbidden()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);

        var result = await _handler.Handle(new GetSafeTenantSettingsQuery(OtherTenantId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _settings.DidNotReceiveWithAnyArgs().ListSettingsAsync(default, default);
    }

    [Fact]
    public async Task UnknownTenant_IsNotFound()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        _settings.ListSettingsAsync(OwnTenantId, Arg.Any<CancellationToken>()).Returns((TenantSettingsList?)null);

        var result = await _handler.Handle(new GetSafeTenantSettingsQuery(OwnTenantId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }
}
