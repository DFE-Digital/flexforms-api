using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class RefreshTenantConfigurationCommandHandlerTests
{
    private readonly ITenantConfigurationProvider _provider = Substitute.For<ITenantConfigurationProvider>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly RefreshTenantConfigurationCommandHandler _handler;

    public RefreshTenantConfigurationCommandHandlerTests()
    {
        _handler = new RefreshTenantConfigurationCommandHandler(
            _provider, _tenantContext, _permissionChecker);
        _provider.Source.Returns("Database");
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenNotTenantAdmin()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(false);

        var result = await _handler.Handle(new RefreshTenantConfigurationCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _provider.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantAdmin_ShouldOnlySeeOwnTenantInResponse()
    {
        var ownId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var otherId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);
        _tenantContext.CurrentTenant.Returns(CreateTenant(ownId, "Transfers"));
        _provider.GetAllTenants().Returns(
        [
            CreateTenant(ownId, "Transfers"),
            CreateTenant(otherId, "Lsrp")
        ]);

        var result = await _handler.Handle(new RefreshTenantConfigurationCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TenantCount);
        var only = Assert.Single(result.Value.Tenants);
        Assert.Equal(ownId, only.Id);
        Assert.Equal("Transfers", only.Name);
        await _provider.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
        // Tenant Admin must not enumerate catalogue via GetAllTenants for the response.
        _ = _provider.DidNotReceive().GetAllTenants();
    }

    [Fact]
    public async Task Handle_SuperAdmin_ShouldSeeFullCatalogue()
    {
        var ownId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var otherId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(ownId, "Transfers"));
        _provider.GetAllTenants().Returns(
        [
            CreateTenant(ownId, "Transfers"),
            CreateTenant(otherId, "Lsrp")
        ]);

        var result = await _handler.Handle(new RefreshTenantConfigurationCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TenantCount);
        Assert.Contains(result.Value.Tenants, t => t.Id == ownId);
        Assert.Contains(result.Value.Tenants, t => t.Id == otherId);
    }

    private static TenantConfiguration CreateTenant(Guid id, string name) =>
        new(id, name, new ConfigurationBuilder().Build(), Array.Empty<string>());
}
