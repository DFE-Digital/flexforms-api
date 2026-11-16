using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

internal class ConfirmTestAuthAuthenticatorSetupCommandValidator : AbstractValidator<ConfirmTestAuthAuthenticatorSetupCommand>
{
    public ConfirmTestAuthAuthenticatorSetupCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Authenticator app code is required")
            .Matches(@"^\s*\d{6}\s*$")
            .WithMessage("Authenticator app code must be 6 digits");
    }
}
