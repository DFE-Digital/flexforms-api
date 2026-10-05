using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.ReportingExport;

public class UpdateReportingExportDefaultCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly ReportingExportChangeResultDto Applied = new(ReportingExportChangeStatus.Applied, 4, 1, [], Guid.NewGuid());

    private readonly IReportingExportPolicyService _policyService = Substitute.For<IReportingExportPolicyService>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantTemplateResolver _templateResolver = Substitute.For<ITenantTemplateResolver>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly UpdateReportingExportDefaultCommandHandler _handler;

    public UpdateReportingExportDefaultCommandHandlerTests()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(new TenantConfiguration(TenantId, "Transfers", new ConfigurationBuilder().Build(), Array.Empty<string>()));
        _templateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateId), Arg.Any<CancellationToken>()).Returns(true);
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "ada@example.gov.uk")], "Test")),
        });
        _handler = new UpdateReportingExportDefaultCommandHandler(
            _policyService, _tenantContext, _permissionChecker, _templateResolver, _httpContextAccessor);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_ShouldChangeTenantOrTemplateDefaultAsSignedInAdmin(bool forTemplate)
    {
        Guid? templateId = forTemplate ? TemplateId : null;
        _policyService.ChangeDefaultAsync(TenantId, templateId, Arg.Any<UpdateReportingExportDefaultRequest>(), "ada@example.gov.uk", Arg.Any<CancellationToken>())
            .Returns(Applied);

        var result = await _handler.Handle(
            new UpdateReportingExportDefaultCommand(TenantId, templateId, ReportingExportModeSetting.ExportAll, "Agreed with DPO"),
            CancellationToken.None);

        Assert.Same(Applied, result.Value);
        await _policyService.Received(1).ChangeDefaultAsync(
            TenantId,
            templateId,
            Arg.Is<UpdateReportingExportDefaultRequest>(r => r.Mode == ReportingExportModeSetting.ExportAll && r.Reason == "Agreed with DPO"),
            "ada@example.gov.uk",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotCheckTemplate_ForTenantDefault()
    {
        _policyService.ChangeDefaultAsync(TenantId, null, Arg.Any<UpdateReportingExportDefaultRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Applied);

        await _handler.Handle(new UpdateReportingExportDefaultCommand(TenantId, null, ReportingExportModeSetting.ApproveFirst, null), CancellationToken.None);

        await _templateResolver.DidNotReceiveWithAnyArgs().IsTemplateInCurrentTenantAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTemplateBelongsToAnotherTenant()
    {
        _templateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(
            new UpdateReportingExportDefaultCommand(TenantId, TemplateId, ReportingExportModeSetting.ApproveFirst, null),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().ChangeDefaultAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenRouteTenantDoesNotMatchCurrentTenant()
    {
        var result = await _handler.Handle(
            new UpdateReportingExportDefaultCommand(Guid.NewGuid(), null, ReportingExportModeSetting.ApproveFirst, null),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().ChangeDefaultAsync(default, default, default!, default!, default);
    }
}
