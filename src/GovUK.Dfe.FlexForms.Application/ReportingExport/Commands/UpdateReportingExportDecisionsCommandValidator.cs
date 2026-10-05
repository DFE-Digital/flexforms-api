using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;

public sealed class UpdateReportingExportDecisionsCommandValidator : AbstractValidator<UpdateReportingExportDecisionsCommand>
{
    public UpdateReportingExportDecisionsCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.Decisions)
            .NotEmpty().WithMessage("Choose at least one field to change.")
            .Must(d => d.Count <= 500).WithMessage("Change at most 500 fields at a time.");
        RuleForEach(x => x.Decisions).ChildRules(decision =>
        {
            decision.RuleFor(d => d.FieldId).NotEmpty().MaximumLength(200);
            decision.RuleFor(d => d.ParentFieldId).MaximumLength(200);
            decision.RuleFor(d => d.Decision).IsInEnum();
            decision.RuleFor(d => d.Reason).MaximumLength(1000);
        });
    }
}
