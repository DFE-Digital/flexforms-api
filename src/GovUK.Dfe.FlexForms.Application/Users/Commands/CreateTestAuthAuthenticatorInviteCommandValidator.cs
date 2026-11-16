using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

internal class CreateTestAuthAuthenticatorInviteCommandValidator : AbstractValidator<CreateTestAuthAuthenticatorInviteCommand>
{
    public CreateTestAuthAuthenticatorInviteCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email address is required")
            .EmailAddress()
            .WithMessage("Email address must be valid");
    }
}
