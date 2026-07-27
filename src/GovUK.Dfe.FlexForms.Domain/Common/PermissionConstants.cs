namespace GovUK.Dfe.FlexForms.Domain.Common;

/// <summary>
/// Well-known permission resource keys used for tenant-wide or wildcard access grants.
/// </summary>
public static class PermissionConstants
{
    /// <summary>
    /// Resource key indicating access applies to any resource of the given type within the tenant.
    /// </summary>
    public const string AnyResourceKey = "Any";

    /// <summary>
    /// Resource key for administering templates in the current tenant
    /// (create, edit schema versions, publish). Distinct from <see cref="AnyResourceKey"/>,
    /// which for Template means create applications on any form.
    /// </summary>
    public const string ManageResourceKey = "Manage";
}
