using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

internal class ConfirmTestAuthAuthenticatorInviteSetupCommandValidator
    : AbstractValidator<ConfirmTestAuthAuthenticatorInviteSetupCommand>
{
    public ConfirmTestAuthAuthenticatorInviteSetupCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email address is required")
            .EmailAddress()
            .WithMessage("Email address must be valid");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Authenticator app code is required")
            .Matches(@"^\s*\d{6}\s*$")
            .WithMessage("Authenticator app code must be 6 digits");
    }
}
