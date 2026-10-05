using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.ReportingExport;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.ReportingExport;

public class ReportingExportHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly ReportingExportChangeResultDto Applied = new(ReportingExportChangeStatus.Applied, 4, 1, [], Guid.NewGuid());

    private readonly IReportingExportAccess _access = Substitute.For<IReportingExportAccess>();
    private readonly IPrismExportControlClient _prism = Substitute.For<IPrismExportControlClient>();

    public ReportingExportHandlerTests()
    {
        _access.ActingUser.Returns("ada@example.gov.uk");
    }

    [Fact]
    public async Task GetPolicy_ShouldNotCallPrism_WhenDenied()
    {
        Deny(TemplateId, DomainErrorCode.NotFound);

        var result = await new GetReportingExportPolicyQueryHandler(_access, _prism)
            .Handle(new GetReportingExportPolicyQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        await _prism.DidNotReceiveWithAnyArgs().GetPolicyAsync(default, default, default);
    }

    [Fact]
    public async Task GetPolicy_ShouldReturnPrismPolicy_WhenAllowed()
    {
        var policy = new ReportingExportPolicyDto(TemplateId, 3, ReportingExportMode.ApproveFirst, ReportingExportDefaultSource.BuiltIn, []);
        _prism.GetPolicyAsync(TenantId, TemplateId, Arg.Any<CancellationToken>()).Returns(Result<ReportingExportPolicyDto>.Success(policy));

        var result = await new GetReportingExportPolicyQueryHandler(_access, _prism)
            .Handle(new GetReportingExportPolicyQuery(TenantId, TemplateId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(policy, result.Value);
    }

    [Fact]
    public async Task UpdateDecisions_ShouldForwardActingUser()
    {
        IReadOnlyList<ReportingExportDecisionRequest> decisions =
            [new ReportingExportDecisionRequest { FieldId = "email", Decision = ReportingExportDecision.Denied, Reason = "Personal data" }];
        _prism.ChangeDecisionsAsync(TenantId, TemplateId, decisions, "ada@example.gov.uk", Arg.Any<CancellationToken>())
            .Returns(Result<ReportingExportChangeResultDto>.Success(Applied));

        var result = await new UpdateReportingExportDecisionsCommandHandler(_access, _prism)
            .Handle(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, decisions), CancellationToken.None);

        Assert.Same(Applied, result.Value);
    }

    [Fact]
    public async Task UpdateDecisions_ShouldNotCallPrism_WhenDenied()
    {
        Deny(TemplateId, DomainErrorCode.Forbidden);

        var result = await new UpdateReportingExportDecisionsCommandHandler(_access, _prism)
            .Handle(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, []), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _prism.DidNotReceiveWithAnyArgs().ChangeDecisionsAsync(default, default, default!, default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateDefault_ShouldTargetTenantOrTemplate(bool forTemplate)
    {
        Guid? templateId = forTemplate ? TemplateId : null;
        _prism.ChangeDefaultAsync(TenantId, templateId, Arg.Any<UpdateReportingExportDefaultRequest>(), "ada@example.gov.uk", Arg.Any<CancellationToken>())
            .Returns(Result<ReportingExportChangeResultDto>.Success(Applied));

        var result = await new UpdateReportingExportDefaultCommandHandler(_access, _prism)
            .Handle(new UpdateReportingExportDefaultCommand(TenantId, templateId, ReportingExportModeSetting.ExportAll, "Agreed with DPO"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _access.Received(1).CheckAsync(TenantId, templateId, Arg.Any<CancellationToken>());
        await _prism.Received(1).ChangeDefaultAsync(
            TenantId,
            templateId,
            Arg.Is<UpdateReportingExportDefaultRequest>(r => r.Mode == ReportingExportModeSetting.ExportAll && r.Reason == "Agreed with DPO"),
            "ada@example.gov.uk",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRefresh_ShouldCheckTenantOnly()
    {
        var refreshId = Guid.NewGuid();
        _prism.GetRefreshAsync(TenantId, refreshId, Arg.Any<CancellationToken>())
            .Returns(Result<ReportingExportRefreshDto>.Success(new(refreshId, ReportingExportRefreshStatus.Running, 10, 4, null, DateTime.UtcNow, DateTime.UtcNow, null)));

        var result = await new GetReportingExportRefreshQueryHandler(_access, _prism)
            .Handle(new GetReportingExportRefreshQuery(TenantId, refreshId), CancellationToken.None);

        Assert.Equal(ReportingExportRefreshStatus.Running, result.Value!.Status);
        await _access.Received(1).CheckAsync(TenantId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DefaultValidator_ShouldRequireReason_ForExportAll()
    {
        var validator = new UpdateReportingExportDefaultCommandValidator();

        Assert.False(validator.Validate(new UpdateReportingExportDefaultCommand(TenantId, null, ReportingExportModeSetting.ExportAll, " ")).IsValid);
        Assert.True(validator.Validate(new UpdateReportingExportDefaultCommand(TenantId, null, ReportingExportModeSetting.ApproveFirst, null)).IsValid);
        Assert.False(validator.Validate(new UpdateReportingExportDefaultCommand(TenantId, Guid.Empty, ReportingExportModeSetting.Inherit, null)).IsValid);
    }

    [Fact]
    public void DecisionsValidator_ShouldRejectEmptyAndUnknownFields()
    {
        var validator = new UpdateReportingExportDecisionsCommandValidator();

        Assert.False(validator.Validate(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, [])).IsValid);
        Assert.False(validator.Validate(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId,
            [new ReportingExportDecisionRequest { FieldId = "", Decision = ReportingExportDecision.Allowed }])).IsValid);
        Assert.True(validator.Validate(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId,
            [new ReportingExportDecisionRequest { FieldId = "email", Decision = ReportingExportDecision.Allowed }])).IsValid);
    }

    private void Deny(Guid? templateId, DomainErrorCode code) =>
        _access.CheckAsync(TenantId, templateId, Arg.Any<CancellationToken>()).Returns(new ReportingExportDenial(code, "No"));
}
