using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandValidators.ReportingExport;

public class UpdateReportingExportDecisionsCommandValidatorTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly UpdateReportingExportDecisionsCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldSucceed_WhenDecisionsValid()
    {
        var result = _validator.Validate(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId,
            [new ReportingExportDecisionRequest { FieldId = "email", Decision = ReportingExportDecision.Allowed }]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ShouldFail_WhenNoDecisions()
    {
        var result = _validator.Validate(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, []));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Choose at least one field to change.");
    }

    [Fact]
    public void Validate_ShouldFail_WhenTooManyDecisions()
    {
        var decisions = Enumerable.Range(0, 501)
            .Select(i => new ReportingExportDecisionRequest { FieldId = $"f{i}", Decision = ReportingExportDecision.Denied })
            .ToList();

        var result = _validator.Validate(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, decisions));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Change at most 500 fields at a time.");
    }

    [Fact]
    public void Validate_ShouldFail_WhenFieldIdMissing()
    {
        var result = _validator.Validate(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId,
            [new ReportingExportDecisionRequest { FieldId = "", Decision = ReportingExportDecision.Allowed }]));

        Assert.False(result.IsValid);
    }
}
