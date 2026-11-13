using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

internal class RedeemTestAuthAuthenticatorInviteCommandValidator : AbstractValidator<RedeemTestAuthAuthenticatorInviteCommand>
{
    public RedeemTestAuthAuthenticatorInviteCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email address is required")
            .EmailAddress()
            .WithMessage("Email address must be valid");

        RuleFor(x => x.InviteCode)
            .NotEmpty()
            .WithMessage("Setup code is required")
            .MaximumLength(32)
            .WithMessage("Setup code is too long");
    }
}
