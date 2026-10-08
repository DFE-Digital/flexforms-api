using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;

public sealed class UpdateReportingExportDefaultCommandValidator : AbstractValidator<UpdateReportingExportDefaultCommand>
{
    public UpdateReportingExportDefaultCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TemplateId).NotEqual(Guid.Empty);
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(1000);
        RuleFor(x => x.Reason)
            .NotEmpty()
            .When(x => x.Mode == ReportingExportModeSetting.ExportAll)
            .WithMessage("Give a reason for exporting new fields automatically, for example who agreed it.");
    }
}
