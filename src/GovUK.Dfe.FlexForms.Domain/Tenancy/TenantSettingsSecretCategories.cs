namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Categories that must always be stored encrypted in TenantConfig, regardless of UI checkbox.
/// </summary>
public static class TenantSettingsSecretCategories
{
    private static readonly HashSet<string> SecretCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConnectionStrings",
        "AzureAd",
        "InternalServiceAuth",
        "Authorization",
        "DfESignIn",
        "EntraSso",
        "TestAuthentication",
        "Email",
        "AuthProviders"
    };

    public static bool ShouldEncrypt(string category)
        => !string.IsNullOrWhiteSpace(category) && SecretCategories.Contains(category.Trim());

    public static IReadOnlyCollection<string> All => SecretCategories;
}
