using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

internal sealed class RecordFileValidationResultCommandValidator : AbstractValidator<RecordFileValidationResultCommand>
{
    public RecordFileValidationResultCommandValidator()
    {
        RuleFor(x => x.FileId).NotEmpty();
        RuleFor(x => x.Message).MaximumLength(1000);
        RuleFor(x => x.Source).MaximumLength(256);
        RuleFor(x => x.CorrelationId).MaximumLength(128);
    }
}
