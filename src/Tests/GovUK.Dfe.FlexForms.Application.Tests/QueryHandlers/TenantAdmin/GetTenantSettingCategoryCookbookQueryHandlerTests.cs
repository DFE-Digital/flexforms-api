using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.TenantAdmin;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.TenantAdmin;

public class GetTenantSettingCategoryCookbookQueryHandlerTests
{
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly GetTenantSettingCategoryCookbookQueryHandler _handler;

    public GetTenantSettingCategoryCookbookQueryHandlerTests()
    {
        _handler = new GetTenantSettingCategoryCookbookQueryHandler(_permissionChecker);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenNotTenantAdmin()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(false);

        var result = await _handler.Handle(
            new GetTenantSettingCategoryCookbookQuery(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_TenantAdmin_ShouldExcludeSuperAdminOnlyCategories()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);

        var result = await _handler.Handle(
            new GetTenantSettingCategoryCookbookQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(
            result.Value!.Categories,
            e => Assert.False(SuperAdminOnlyTenantSettingCategories.IsRestricted(e.Category)));
        Assert.DoesNotContain(
            result.Value.Categories,
            e => e.Category.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_SuperAdmin_ShouldReturnFullCookbook()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);

        var result = await _handler.Handle(
            new GetTenantSettingCategoryCookbookQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantSettingCategoryCookbook.All.Count, result.Value!.Categories.Count);
        Assert.Contains(
            result.Value.Categories,
            e => e.Category.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase));
    }
}
