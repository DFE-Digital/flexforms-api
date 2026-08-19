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
    }
}
