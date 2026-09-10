using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

internal sealed class GetTenantUsersQueryValidator : AbstractValidator<GetTenantUsersQuery>
{
    public GetTenantUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, GetTenantUsersQuery.MaxPageSize);

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email!)
                .EmailAddress();
        });

        RuleFor(x => x.SearchTerm!)
            .MaximumLength(GetTenantUsersQuery.MaxSearchTermLength)
            .When(x => !string.IsNullOrWhiteSpace(x.SearchTerm));

        RuleFor(x => x.Role!)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.Role));
    }
}
