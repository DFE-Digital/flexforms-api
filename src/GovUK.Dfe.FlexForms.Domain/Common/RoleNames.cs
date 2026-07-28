namespace GovUK.Dfe.FlexForms.Domain.Common;

/// <summary>
/// Well-known role names stored in the Roles table and issued as role claims.
/// <see cref="SuperAdmin"/> is platform-wide; <see cref="Admin"/> and <see cref="User"/>
/// are tenant system roles. Tenant-specific capabilities use custom roles + <c>RolePermissions</c>.
/// </summary>
public static class RoleNames
{
    /// <summary>
    /// Platform administrator. Privileged via <c>IsInRole</c>, not tenant-assignable,
    /// and the name is reserved (cannot be used for custom tenant roles).
    /// </summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>
    /// Tenant administrator. Full access within a tenant, assignable through tenant APIs,
    /// and reserved so tenants cannot create a custom role with the same name.
    /// </summary>
    public const string Admin = "Admin";

    public const string User = "User";

    /// <summary>
    /// Legacy role name retained for existing DB rows and JWT claims.
    /// Not assignable and not privileged — replace with custom roles + RolePermissions.
    /// </summary>
    public const string Caseworker = "Caseworker";

    /// <summary>
    /// Roles that can be assigned through the tenant administrative role assignment API.
    /// </summary>
    public static readonly IReadOnlyCollection<string> Assignable =
    [
        Admin,
        User
    ];

    /// <summary>
    /// Role names reserved for the platform. Tenants must not create custom roles with these names
    /// or assign them through tenant APIs.
    /// </summary>
    public static readonly IReadOnlyCollection<string> Reserved =
    [
        SuperAdmin,
        Admin,
        "Administrator"
    ];

    /// <summary>
    /// Returns true when the role is the privileged platform admin.
    /// </summary>
    public static bool IsSuperAdmin(string? roleName) =>
        string.Equals(roleName, SuperAdmin, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the name is reserved for platform use and must not be used
    /// as a tenant-assignable or custom role name.
    /// </summary>
    public static bool IsReservedRoleName(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        foreach (var reserved in Reserved)
        {
            if (string.Equals(roleName.Trim(), reserved, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when the role can be assigned through the tenant administrative role assignment API.
    /// </summary>
    public static bool IsAssignable(string roleName) =>
        ResolveAssignable(roleName) is not null;

    /// <summary>
    /// Resolves a role name to its canonical tenant-assignable form, or null when not assignable.
    /// </summary>
    public static string? ResolveAssignable(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return null;

        if (string.Equals(roleName, User, StringComparison.OrdinalIgnoreCase))
            return User;

        if (string.Equals(roleName, Admin, StringComparison.OrdinalIgnoreCase))
            return Admin;

        return null;
    }

    /// <summary>
    /// Returns true when assigning <paramref name="targetRole"/> would downgrade a platform
    /// SuperAdmin membership to User.
    /// </summary>
    public static bool IsDowngradeToUser(string? currentRole, string targetRole)
    {
        if (!string.Equals(ResolveAssignable(targetRole) ?? targetRole, User, StringComparison.OrdinalIgnoreCase))
            return false;

        return IsSuperAdmin(currentRole);
    }

    /// <summary>
    /// Resolves a role name from a well-known role identifier.
    /// </summary>
    public static string? FromRoleId(Guid roleId)
    {
        if (roleId == RoleConstants.AdminRoleId)
            return SuperAdmin;

        if (roleId == RoleConstants.CaseworkerRoleId)
            return Caseworker;

        if (roleId == RoleConstants.UserRoleId)
            return User;

        return null;
    }
}
