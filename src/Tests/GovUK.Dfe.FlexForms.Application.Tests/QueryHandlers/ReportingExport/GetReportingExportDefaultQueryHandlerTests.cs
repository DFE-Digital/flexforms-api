using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.ReportingExport;

public class GetReportingExportDefaultQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly IReportingExportPolicyService _policyService = Substitute.For<IReportingExportPolicyService>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantTemplateResolver _templateResolver = Substitute.For<ITenantTemplateResolver>();
    private readonly GetReportingExportDefaultQueryHandler _handler;

    public GetReportingExportDefaultQueryHandlerTests()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(TenantId));
        _templateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateId), Arg.Any<CancellationToken>()).Returns(true);
        _handler = new GetReportingExportDefaultQueryHandler(_policyService, _tenantContext, _permissionChecker, _templateResolver);
    }

    [Fact]
    public async Task Handle_ShouldReturnTenantDefault_WithoutCheckingTemplate()
    {
        var exportDefault = new ReportingExportDefaultDto(
            null, ReportingExportModeSetting.Inherit, ReportingExportMode.ApproveFirst, ReportingExportDefaultSource.BuiltIn, null, null, null);
        _policyService.GetDefaultAsync(TenantId, null, Arg.Any<CancellationToken>()).Returns(exportDefault);

        var result = await _handler.Handle(new GetReportingExportDefaultQuery(TenantId, null), CancellationToken.None);

        Assert.Same(exportDefault, result.Value);
        await _templateResolver.DidNotReceiveWithAnyArgs().IsTemplateInCurrentTenantAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTemplateBelongsToAnotherTenant()
    {
        _templateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(new GetReportingExportDefaultQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().GetDefaultAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotInteractiveTenantAdmin()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(false);

        var result = await _handler.Handle(new GetReportingExportDefaultQuery(TenantId, null), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenReportingIsNotSetUp()
    {
        _policyService.GetDefaultAsync(TenantId, TemplateId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Reporting export is not set up in this environment."));

        var result = await _handler.Handle(new GetReportingExportDefaultQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Reporting export is not set up in this environment.", result.Error);
    }

    private static TenantConfiguration CreateTenant(Guid id) =>
        new(id, "Transfers", new ConfigurationBuilder().Build(), Array.Empty<string>());
}
