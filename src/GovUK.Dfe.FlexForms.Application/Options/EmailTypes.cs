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
    public const string BugReportInternal = "BugReportInternal";
    public const string BugReportUser = "BugReportUser";
    public const string FeedbackOrSuggestionInternal = "FeedbackOrSuggestionInternal";
    public const string SupportRequestInternal = "SupportRequestInternal";
    public const string SupportRequestUser = "SupportRequestUser";
    public const string TestAuthPassword = "TestAuthPasswordEmail";

    /// <summary>
    /// Notify template keys a form can override. <see cref="TestAuthPassword"/> stays tenant-wide.
    /// </summary>
    public static readonly string[] FormTemplateKeys =
    [
        ApplicationSubmitted,
        ContributorInvited,
        BugReportInternal,
        BugReportUser,
        FeedbackOrSuggestionInternal,
        SupportRequestInternal,
        SupportRequestUser
    ];
}
