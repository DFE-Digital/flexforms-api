using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

internal class VerifyTestAuthPasswordCommandValidator : AbstractValidator<VerifyTestAuthPasswordCommand>
{
    public VerifyTestAuthPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email address is required")
            .EmailAddress()
            .WithMessage("Email address must be valid");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("One-time password is required")
            .Matches(@"^\s*\d{6}\s*$")
            .WithMessage("One-time password must be 6 digits");
    }
}
