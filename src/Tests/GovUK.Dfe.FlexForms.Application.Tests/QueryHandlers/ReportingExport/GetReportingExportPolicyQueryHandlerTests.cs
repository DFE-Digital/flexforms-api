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

public class GetReportingExportPolicyQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly IReportingExportPolicyService _policyService = Substitute.For<IReportingExportPolicyService>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantTemplateResolver _templateResolver = Substitute.For<ITenantTemplateResolver>();
    private readonly GetReportingExportPolicyQueryHandler _handler;

    public GetReportingExportPolicyQueryHandlerTests()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(TenantId));
        _templateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateId), Arg.Any<CancellationToken>()).Returns(true);
        _handler = new GetReportingExportPolicyQueryHandler(_policyService, _tenantContext, _permissionChecker, _templateResolver);
    }

    [Fact]
    public async Task Handle_ShouldReturnPolicy_WhenAdminOfTenantOwningTemplate()
    {
        var policy = new ReportingExportPolicyDto(TemplateId, 3, ReportingExportMode.ApproveFirst, ReportingExportDefaultSource.BuiltIn, []);
        _policyService.GetPolicyAsync(TenantId, TemplateId, Arg.Any<CancellationToken>()).Returns(policy);

        var result = await _handler.Handle(new GetReportingExportPolicyQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(policy, result.Value);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotInteractiveTenantAdmin()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(false);

        var result = await _handler.Handle(new GetReportingExportPolicyQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().GetPolicyAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenRouteTenantDoesNotMatchCurrentTenant()
    {
        var result = await _handler.Handle(new GetReportingExportPolicyQuery(Guid.NewGuid(), TemplateId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().GetPolicyAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTemplateBelongsToAnotherTenant()
    {
        _templateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(new GetReportingExportPolicyQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().GetPolicyAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_ShouldExplainUncataloguedTemplate_WhenReportingDoesNotKnowIt()
    {
        _policyService.GetPolicyAsync(TenantId, TemplateId, Arg.Any<CancellationToken>()).Returns((ReportingExportPolicyDto?)null);

        var result = await _handler.Handle(new GetReportingExportPolicyQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Contains("not known to reporting yet", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenReportingIsUnavailable()
    {
        _policyService.GetPolicyAsync(TenantId, TemplateId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("The reporting service could not be reached."));

        var result = await _handler.Handle(new GetReportingExportPolicyQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("The reporting service could not be reached.", result.Error);
    }

    private static TenantConfiguration CreateTenant(Guid id) =>
        new(id, "Transfers", new ConfigurationBuilder().Build(), Array.Empty<string>());
}
