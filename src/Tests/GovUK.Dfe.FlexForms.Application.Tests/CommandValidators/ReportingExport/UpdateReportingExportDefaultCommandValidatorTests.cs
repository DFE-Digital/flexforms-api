using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandValidators.ReportingExport;

public class UpdateReportingExportDefaultCommandValidatorTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private readonly UpdateReportingExportDefaultCommandValidator _validator = new();

    [Theory]
    [InlineData(ReportingExportModeSetting.ApproveFirst, null)]
    [InlineData(ReportingExportModeSetting.Inherit, null)]
    [InlineData(ReportingExportModeSetting.ExportAll, "Agreed with DPO")]
    public void Validate_ShouldSucceed_WhenValid(ReportingExportModeSetting mode, string? reason)
    {
        Assert.True(_validator.Validate(new UpdateReportingExportDefaultCommand(TenantId, null, mode, reason)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Validate_ShouldRequireReason_ForExportAll(string? reason)
    {
        var result = _validator.Validate(new UpdateReportingExportDefaultCommand(TenantId, null, ReportingExportModeSetting.ExportAll, reason));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Reason");
    }

    [Fact]
    public void Validate_ShouldFail_WhenTemplateIdEmpty()
    {
        var result = _validator.Validate(new UpdateReportingExportDefaultCommand(TenantId, Guid.Empty, ReportingExportModeSetting.Inherit, null));

        Assert.Contains(result.Errors, e => e.PropertyName == "TemplateId");
    }
}
