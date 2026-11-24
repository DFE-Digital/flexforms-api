using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Tenancy;

public class TenantSafeSettingCategoriesTests
{
    private static readonly string[] SharedCategories =
    [
        "EventMappings",
        "EmailPlaceholderMappings",
        "SchemaEvents",
        "EventTriggers",
        "FileValidation"
    ];

    private static readonly string[] WebCategories =
    [
        "ApplicationTerminology",
        "NotificationBanner",
        "Dashboard",
        "ApplicationPreview",
        "Contributors",
        "TaskList",
        "ApplicationSubmittedPage"
    ];

    [Fact]
    public void All_IsExactlyTheTwelveDelegatedCategories()
    {
        Assert.Equal(
            SharedCategories.Concat(WebCategories).Order(StringComparer.Ordinal),
            TenantSafeSettingCategories.All.Order(StringComparer.Ordinal));
    }

    public static TheoryData<string> Shared() => new(SharedCategories);

    public static TheoryData<string> Web() => new(WebCategories);

    [Theory]
    [MemberData(nameof(Shared))]
    public void ApiReadCategories_AreStoredUnderShared(string category)
    {
        Assert.True(TenantSafeSettingCategories.IsSafe(category));
        Assert.Equal(TenantSafeSettingCategories.SharedTarget, TenantSafeSettingCategories.TargetFor(category));
    }

    [Theory]
    [MemberData(nameof(Web))]
    public void WebOnlyCategories_AreStoredUnderWeb(string category)
    {
        Assert.True(TenantSafeSettingCategories.IsSafe(category));
        Assert.Equal(TenantSafeSettingCategories.DefaultTarget, TenantSafeSettingCategories.TargetFor(category));
    }

    [Theory]
    [InlineData("  dashboard  ", "Web")]
    [InlineData("FILEVALIDATION", "Shared")]
    [InlineData(" eventmappings", "Shared")]
    public void CategoryNames_IgnoreCaseAndSurroundingSpaces(string category, string expectedTarget)
    {
        Assert.True(TenantSafeSettingCategories.IsSafe(category));
        Assert.Equal(expectedTarget, TenantSafeSettingCategories.TargetFor(category));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Layout")]
    [InlineData("Dashboards")]
    public void OtherValues_AreNotSafeAndDefaultToWeb(string? category)
    {
        Assert.False(TenantSafeSettingCategories.IsSafe(category));
        Assert.Equal(TenantSafeSettingCategories.DefaultTarget, TenantSafeSettingCategories.TargetFor(category));
    }

    public static TheoryData<string> SuperAdminOnly() => new(SuperAdminOnlyTenantSettingCategories.All);

    [Theory]
    [MemberData(nameof(SuperAdminOnly))]
    public void SuperAdminOnlyCategories_AreNeverSafe(string category)
    {
        Assert.False(TenantSafeSettingCategories.IsSafe(category));
        Assert.False(TenantSafeSettingCategories.IsSafe(category.ToLowerInvariant()));
    }

    public static TheoryData<string> Encrypted() => new(TenantSettingsSecretCategories.All);

    [Theory]
    [MemberData(nameof(Encrypted))]
    public void EncryptedCategories_AreNeverSafe(string category)
    {
        Assert.False(TenantSafeSettingCategories.IsSafe(category));
    }
}
