using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.ReportingExport;

public class GetReportingExportRefreshQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private readonly IReportingExportPolicyService _policyService = Substitute.For<IReportingExportPolicyService>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly GetReportingExportRefreshQueryHandler _handler;

    public GetReportingExportRefreshQueryHandlerTests()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(new TenantConfiguration(TenantId, "Transfers", new ConfigurationBuilder().Build(), Array.Empty<string>()));
        _handler = new GetReportingExportRefreshQueryHandler(_policyService, _tenantContext, _permissionChecker);
    }

    [Fact]
    public async Task Handle_ShouldReturnRefresh_WhenItBelongsToTheTenant()
    {
        var refreshId = Guid.NewGuid();
        _policyService.GetRefreshAsync(TenantId, refreshId, Arg.Any<CancellationToken>())
            .Returns(new ReportingExportRefreshDto(refreshId, ReportingExportRefreshStatus.Running, 10, 4, null, DateTime.UtcNow, DateTime.UtcNow, null));

        var result = await _handler.Handle(new GetReportingExportRefreshQuery(TenantId, refreshId), CancellationToken.None);

        Assert.Equal(ReportingExportRefreshStatus.Running, result.Value!.Status);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenReportingDoesNotKnowTheRefresh()
    {
        _policyService.GetRefreshAsync(TenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ReportingExportRefreshDto?)null);

        var result = await _handler.Handle(new GetReportingExportRefreshQuery(TenantId, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenRouteTenantDoesNotMatchCurrentTenant()
    {
        var result = await _handler.Handle(new GetReportingExportRefreshQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().GetRefreshAsync(default, default, default);
    }
}
