using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin.TenantAdminTestData;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class DeleteTenantSettingCommandHandlerTests
{
    private readonly ITenantSettingsWriter _writer = Substitute.For<ITenantSettingsWriter>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly ITenantSettingAuditWriter _audit = Substitute.For<ITenantSettingAuditWriter>();
    private readonly DeleteTenantSettingCommandHandler _handler;

    public DeleteTenantSettingCommandHandlerTests()
    {
        _tenantContext.CurrentTenant.Returns(Tenant());
        _writer.DeleteSettingAsync(default, default!, default!, default)
            .ReturnsForAnyArgs(ci => new DeleteTenantSettingResult(Guid.NewGuid(), ci.ArgAt<string>(1), ci.ArgAt<string>(2), WasSecret: true));
        _handler = new DeleteTenantSettingCommandHandler(_writer, _tenantContext, _permissions, _configProvider, _audit, SignedInAs());
    }

    [Fact]
    public async Task TenantAdmin_DeletesADelegatedSetting_AndAuditsIt()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);

        var result = await HandleAsync(" Dashboard ", " Web ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Setting 'Dashboard' (Target=Web) deleted successfully.", result.Value!.Message);
        await _writer.Received(1).DeleteSettingAsync(OwnTenantId, "Dashboard", "Web", Arg.Any<CancellationToken>());
        await _audit.Received(1).AppendAsync(OwnTenantId, "Dashboard", "Web", "Deleted", ActorEmail, true, Arg.Any<CancellationToken>());
        await _configProvider.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotAnInteractiveTenantAdmin_IsForbidden()
    {
        var result = await HandleAsync("Dashboard", "Web");

        AssertRefused(result, DomainErrorCode.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherTenantOrNoTenantContext_IsForbidden(bool noTenantContext)
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        if (noTenantContext)
            _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await HandleAsync("Dashboard", "Web", noTenantContext ? OwnTenantId : OtherTenantId);

        AssertRefused(result, DomainErrorCode.Forbidden);
    }

    [Theory]
    [InlineData("", "Web")]
    [InlineData("Dashboard", " ")]
    public async Task MissingCategoryOrTarget_FailsValidation(string category, string target)
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);

        var result = await HandleAsync(category, target);

        AssertRefused(result, DomainErrorCode.Validation);
    }

    [Theory]
    [MemberData(nameof(SuperAdminOnlyCategories), MemberType = typeof(TenantAdminTestData))]
    public async Task SuperAdminOnlyCategory_IsForbiddenForATenantAdmin(string category)
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);

        var result = await HandleAsync(category, "Shared");

        AssertRefused(result, DomainErrorCode.Forbidden);
        Assert.Equal($"Only SuperAdmin can delete '{category}' settings.", result.Error);
    }

    [Fact]
    public async Task SuperAdminOnlyCategory_CanBeDeletedByASuperAdmin()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        _permissions.IsInteractivePlatformAdmin().Returns(true);

        var result = await HandleAsync("ConnectionStrings", "Shared");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SettingThatDoesNotExist_IsNotFound()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        _writer.DeleteSettingAsync(default, default!, default!, default).ReturnsForAnyArgs((DeleteTenantSettingResult?)null);

        var result = await HandleAsync("Dashboard", "Web");

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        await _audit.DidNotReceiveWithAnyArgs().AppendAsync(default, default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task TenantMissingFromTheStore_IsNotFound()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        _writer.DeleteSettingAsync(default, default!, default!, default).ThrowsAsyncForAnyArgs(new KeyNotFoundException("Tenant not found."));

        var result = await HandleAsync("Dashboard", "Web");

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    private void AssertRefused(Result<DeleteTenantSettingResponse> result, DomainErrorCode code)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ErrorCode);
        _writer.DidNotReceiveWithAnyArgs().DeleteSettingAsync(default, default!, default!, default);
        _configProvider.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    private Task<Result<DeleteTenantSettingResponse>> HandleAsync(string category, string target, Guid? tenantId = null) =>
        _handler.Handle(new DeleteTenantSettingCommand(tenantId ?? OwnTenantId, category, target), CancellationToken.None);
}
