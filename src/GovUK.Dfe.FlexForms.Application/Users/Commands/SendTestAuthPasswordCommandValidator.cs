using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

internal class SendTestAuthPasswordCommandValidator : AbstractValidator<SendTestAuthPasswordCommand>
{
    public SendTestAuthPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email address is required")
            .EmailAddress()
            .WithMessage("Email address must be valid");
    }
}
