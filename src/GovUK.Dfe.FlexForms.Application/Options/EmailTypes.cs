namespace GovUK.Dfe.FlexForms.Application.Options;

/// <summary>
/// Well-known email type keys used with <c>EmailPlaceholderMappings</c> and
/// <see cref="Services.IEmailTemplateResolver"/>.
/// </summary>
public static class EmailTypes
{
    public const string ApplicationSubmitted = "ApplicationSubmitted";
    public const string ContributorInvited = "ContributorInvited";
    public const string ContributorAccessGranted = "ContributorAccessGranted";
}
