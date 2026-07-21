using FluentValidation;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GovUK.Dfe.FlexForms.Application.Tests")]
namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

internal class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.SubjectToken)
            .NotEmpty()
            .WithMessage("Subject token is required");
    }
}
