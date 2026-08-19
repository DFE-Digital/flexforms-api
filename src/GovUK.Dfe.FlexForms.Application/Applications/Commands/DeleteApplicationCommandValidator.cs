using FluentValidation;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GovUK.Dfe.FlexForms.Application.Tests")]
namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

internal class DeleteApplicationCommandValidator : AbstractValidator<DeleteApplicationCommand>
{
    public DeleteApplicationCommandValidator()
    {
        RuleFor(x => x.ApplicationId)
            .NotEmpty()
            .WithMessage("Application ID is required");
    }
} 
