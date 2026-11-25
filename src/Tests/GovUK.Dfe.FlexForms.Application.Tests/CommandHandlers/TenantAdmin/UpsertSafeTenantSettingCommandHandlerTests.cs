using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin.TenantAdminTestData;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class UpsertSafeTenantSettingCommandHandlerTests
{
    private const string DashboardJson = """{"EnableFilters":true}""";

    private readonly ITenantSettingsWriter _writer = Substitute.For<ITenantSettingsWriter>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly ITenantSettingAuditWriter _audit = Substitute.For<ITenantSettingAuditWriter>();
    private IHttpContextAccessor _httpContext = SignedInAs();

    public UpsertSafeTenantSettingCommandHandlerTests()
    {
        _tenantContext.CurrentTenant.Returns(Tenant());
        _writer.UpsertSettingAsync(default, default!, default!, default!, default, default)
            .ReturnsForAnyArgs(ci => new UpsertTenantSettingResult(Guid.NewGuid(), true, ci.ArgAt<string>(1), ci.ArgAt<string>(2)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InteractiveAdminOrSuperAdmin_SavesTheSetting(bool tenantAdmin, bool superAdmin)
    {
        _permissions.IsInteractiveTenantAdmin().Returns(tenantAdmin);
        _permissions.IsInteractivePlatformAdmin().Returns(superAdmin);

        var result = await HandleAsync("Dashboard", DashboardJson);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.WasCreated);
        Assert.Equal("Organisation setting 'Dashboard' created successfully.", result.Value.Message);
        await _writer.Received(1).UpsertSettingAsync(OwnTenantId, "Dashboard", "Web", DashboardJson, false, Arg.Any<CancellationToken>());
        await _audit.Received(1).AppendAsync(OwnTenantId, "Dashboard", "Web", "Created", ActorEmail, false, Arg.Any<CancellationToken>());
        await _configProvider.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonAdminOrServicePrincipal_IsForbidden()
    {
        var result = await HandleAsync("Dashboard", DashboardJson);

        AssertRefused(result, DomainErrorCode.Forbidden);
    }

    [Fact]
    public async Task NoTenantContext_IsForbidden()
    {
        AsTenantAdmin();
        _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await HandleAsync("Dashboard", DashboardJson);

        AssertRefused(result, DomainErrorCode.Forbidden);
    }

    [Fact]
    public async Task AnotherTenant_IsForbidden()
    {
        AsTenantAdmin();

        var result = await HandleAsync("Dashboard", DashboardJson, OtherTenantId);

        AssertRefused(result, DomainErrorCode.Forbidden);
        Assert.Contains(OwnTenantId.ToString(), result.Error);
    }

    [Theory]
    [InlineData("Layout")]
    [InlineData("ConnectionStrings")]
    [InlineData("EntraSso")]
    [InlineData("TestAuthentication")]
    [InlineData("")]
    public async Task CategoryThatIsNotDelegated_IsRefused(string category)
    {
        AsTenantAdmin();

        var result = await HandleAsync(category, DashboardJson);

        AssertRefused(result, DomainErrorCode.BadRequest);
        Assert.Contains("is not a delegated organisation setting", result.Error);
    }

    [Fact]
    public async Task SettingsThatAreNotBase64_AreRefused()
    {
        AsTenantAdmin();

        var result = await HandleAsync("Dashboard", rawSettings: "not base64!");

        AssertRefused(result, DomainErrorCode.BadRequest);
        Assert.Equal("Invalid Base64 format for SettingsJson", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptySettings_AreRefused(string json)
    {
        AsTenantAdmin();

        var result = await HandleAsync("Dashboard", json);

        AssertRefused(result, DomainErrorCode.BadRequest);
        Assert.Equal("Settings JSON is required.", result.Error);
    }

    [Fact]
    public async Task InvalidJson_FailsValidation()
    {
        AsTenantAdmin();

        var result = await HandleAsync("Dashboard", "{ not json");

        AssertRefused(result, DomainErrorCode.Validation);
    }

    [Fact]
    public async Task SharedCategory_IsSavedUnderSharedAfterRemovingAWebCopy()
    {
        AsTenantAdmin();
        const string json = """{"Mode":"Strict"}""";

        var result = await HandleAsync("FileValidation", json);

        Assert.True(result.IsSuccess);
        Assert.Equal("Shared", result.Value!.Target);
        Received.InOrder(() =>
        {
            _writer.DeleteSettingAsync(OwnTenantId, "FileValidation", "Web", Arg.Any<CancellationToken>());
            _writer.UpsertSettingAsync(OwnTenantId, "FileValidation", "Shared", json, false, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task WebCategory_DoesNotTouchAnotherTarget()
    {
        AsTenantAdmin();

        await HandleAsync("Dashboard", DashboardJson);

        await _writer.DidNotReceiveWithAnyArgs().DeleteSettingAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task UpdatingAnExistingSetting_IsAuditedAsUpdated()
    {
        AsTenantAdmin();
        _writer.UpsertSettingAsync(default, default!, default!, default!, default, default)
            .ReturnsForAnyArgs(new UpsertTenantSettingResult(Guid.NewGuid(), false, "Dashboard", "Web"));

        var result = await HandleAsync("Dashboard", DashboardJson);

        Assert.Equal("Organisation setting 'Dashboard' updated successfully.", result.Value!.Message);
        await _audit.Received(1).AppendAsync(OwnTenantId, "Dashboard", "Web", "Updated", ActorEmail, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TenantMissingFromTheStore_IsNotFound()
    {
        AsTenantAdmin();
        _writer.UpsertSettingAsync(default, default!, default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new KeyNotFoundException("Tenant not found."));

        var result = await HandleAsync("Dashboard", DashboardJson);

        AssertRefused(result, DomainErrorCode.NotFound, writerCalled: true);
    }

    [Fact]
    public async Task TargetConflict_FailsValidation()
    {
        AsTenantAdmin();
        _writer.UpsertSettingAsync(default, default!, default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new TenantSettingTargetConflictException("Exists under another target."));

        var result = await HandleAsync("FileValidation", """{"Mode":"Strict"}""");

        AssertRefused(result, DomainErrorCode.Validation, writerCalled: true);
    }

    [Theory]
    [InlineData("email", "alt@example.com", "alt@example.com")]
    [InlineData(ClaimTypes.Name, "Alt Name", "Alt Name")]
    [InlineData("other", "x", "unknown")]
    public async Task Actor_FallsBackFromEmailToNameToUnknown(string claimType, string value, string expectedActor)
    {
        AsTenantAdmin();
        _httpContext = HttpContextFor(new Claim(claimType, value));

        await HandleAsync("Dashboard", DashboardJson);

        await _audit.Received(1).AppendAsync(OwnTenantId, "Dashboard", "Web", "Created", expectedActor, false, Arg.Any<CancellationToken>());
    }

    private void AsTenantAdmin() => _permissions.IsInteractiveTenantAdmin().Returns(true);

    private void AssertRefused<T>(Result<T> result, DomainErrorCode code, bool writerCalled = false)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ErrorCode);
        if (!writerCalled)
            _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
        _audit.DidNotReceiveWithAnyArgs().AppendAsync(default, default!, default!, default!, default!, default, default);
        _configProvider.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    private Task<Result<UpsertTenantSettingResponse>> HandleAsync(
        string category,
        string? json = null,
        Guid? tenantId = null,
        string? rawSettings = null)
    {
        var handler = new UpsertSafeTenantSettingCommandHandler(_writer, _tenantContext, _permissions, _configProvider, _audit, _httpContext);
        return handler.Handle(
            new UpsertSafeTenantSettingCommand(tenantId ?? OwnTenantId, category, rawSettings ?? ToBase64(json ?? "")),
            CancellationToken.None);
    }
}
