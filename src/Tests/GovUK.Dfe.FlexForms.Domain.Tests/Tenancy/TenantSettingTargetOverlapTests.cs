using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Tenancy;

public class TenantSettingTargetOverlapTests
{
    [Theory]
    [InlineData("Api")]
    [InlineData("Web")]
    public void FindConflictingTargets_AppTarget_ConflictsWithExistingShared(string target)
    {
        var conflicts = TenantSettingTargetOverlap.FindConflictingTargets(
            "InternalServiceAuth",
            target,
            [("InternalServiceAuth", "Shared")]);

        Assert.Equal(new[] { "Shared" }, conflicts);
    }

    [Fact]
    public void FindConflictingTargets_Shared_ConflictsWithExistingAppTargets()
    {
        var conflicts = TenantSettingTargetOverlap.FindConflictingTargets(
            "TestAuthentication",
            "Shared",
            [("TestAuthentication", "Api"), ("TestAuthentication", "Web"), ("Layout", "Web")]);

        Assert.Equal(new[] { "Api", "Web" }, conflicts);
    }

    [Fact]
    public void FindConflictingTargets_ApiAndWeb_DoNotConflict()
    {
        var conflicts = TenantSettingTargetOverlap.FindConflictingTargets(
            "InternalServiceAuth",
            "Web",
            [("InternalServiceAuth", "Api")]);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void FindConflictingTargets_IsCaseInsensitive()
    {
        var conflicts = TenantSettingTargetOverlap.FindConflictingTargets(
            "internalserviceauth",
            "api",
            [("InternalServiceAuth", "shared")]);

        Assert.Single(conflicts);
    }

    [Fact]
    public void FindConflictingTargets_SameTarget_IsNotAConflict()
    {
        var conflicts = TenantSettingTargetOverlap.FindConflictingTargets(
            "InternalServiceAuth",
            "Shared",
            [("InternalServiceAuth", "Shared")]);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void FindOverlaps_ReportsOnlyCategoriesUnderSharedAndAppTarget()
    {
        var overlaps = TenantSettingTargetOverlap.FindOverlaps(
        [
            ("InternalServiceAuth", "Shared"),
            ("InternalServiceAuth", "Api"),
            ("InternalServiceAuth", "Web"),
            ("TestAuthentication", "Api"),
            ("TestAuthentication", "Web"),
            ("Layout", "Shared")
        ]);

        var overlap = Assert.Single(overlaps);
        Assert.Contains("InternalServiceAuth", overlap);
    }
}
