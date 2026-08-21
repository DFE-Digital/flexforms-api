using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Tenancy;

public class SuperAdminOnlyTenantSettingCategoriesTests
{
    [Theory]
    [InlineData("ApplicationTemplates")]
    [InlineData("Template")]
    [InlineData("ConnectionStrings")]
    [InlineData("applicationtemplates")]
    [InlineData("connectionstrings")]
    public void IsRestricted_ShouldBeTrue_ForKnownCategories(string category)
    {
        Assert.True(SuperAdminOnlyTenantSettingCategories.IsRestricted(category));
    }

    [Theory]
    [InlineData("Layout")]
    [InlineData("Dashboard")]
    [InlineData(null)]
    [InlineData("")]
    public void IsRestricted_ShouldBeFalse_ForOtherCategories(string? category)
    {
        Assert.False(SuperAdminOnlyTenantSettingCategories.IsRestricted(category));
    }
}
