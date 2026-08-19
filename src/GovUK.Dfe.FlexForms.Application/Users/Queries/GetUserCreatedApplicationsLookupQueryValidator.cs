using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

internal class GetUserCreatedApplicationsLookupQueryValidator
    : AbstractValidator<GetUserCreatedApplicationsLookupQuery>
{
    public GetUserCreatedApplicationsLookupQueryValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();
    }
}
