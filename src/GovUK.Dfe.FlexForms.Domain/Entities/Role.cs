using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

/// <summary>
/// A named role. Legacy global system roles have <see cref="TenantId"/> null.
/// Tenant-scoped roles (including per-tenant copies of SuperAdmin/User) set <see cref="TenantId"/>.
/// </summary>
public sealed class Role : BaseAggregateRoot, IEntity<RoleId>
{
    public RoleId? Id { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>
    /// When set, this role belongs only to that tenant (shared EA DB isolation).
    /// Null = legacy global role row (kept for <see cref="User.RoleId"/> FK compatibility).
    /// </summary>
    public Guid? TenantId { get; private set; }

    /// <summary>
    /// Platform-seeded system roles (SuperAdmin/User) that tenants should not delete.
    /// </summary>
    public bool IsSystem { get; private set; }

    private readonly List<RolePermission> _permissions = new();

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    private Role() { }

    /// <summary>
    /// Constructs a legacy global role (no tenant scope).
    /// </summary>
    public Role(RoleId id, string name)
        : this(id, name, tenantId: null, isSystem: true)
    {
    }

    /// <summary>
    /// Constructs a role, optionally scoped to a tenant.
    /// </summary>
    public Role(RoleId id, string name, Guid? tenantId, bool isSystem = false)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = NormalizeName(name);
        TenantId = tenantId;
        IsSystem = isSystem;
    }

    /// <summary>
    /// Factory for a tenant-scoped role (system or custom). Prefer
    /// <see cref="CreateCustomForTenant"/> / <see cref="CreateSystemAssignableForTenant"/> for new code.
    /// </summary>
    public static Role CreateForTenant(Guid tenantId, string name, bool isSystem)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new Role(new RoleId(Guid.NewGuid()), name, tenantId, isSystem);
    }

    /// <summary>
    /// Creates a non-system tenant role. Rejects reserved and system-assignable names.
    /// </summary>
    public static Role CreateCustomForTenant(Guid tenantId, string name)
    {
        var normalized = NormalizeName(name);
        EnsureNameAllowedForCustomRole(normalized);
        return CreateForTenant(tenantId, normalized, isSystem: false);
    }

    /// <summary>
    /// Creates a tenant-scoped copy of a system-assignable role (currently <see cref="RoleNames.User"/>).
    /// </summary>
    public static Role CreateSystemAssignableForTenant(Guid tenantId, string name)
    {
        var canonical = RoleNames.ResolveAssignable(name)
            ?? throw new InvalidOperationException(
                $"Role name '{name?.Trim()}' is not a system-assignable tenant role.");

        return CreateForTenant(tenantId, canonical, isSystem: true);
    }

    /// <summary>
    /// Provisions a tenant role: system-assignable names become system roles; other non-reserved
    /// names become custom roles.
    /// </summary>
    public static Role CreateProvisionedForTenant(Guid tenantId, string name)
    {
        if (RoleNames.IsReservedRoleName(name))
        {
            throw new InvalidOperationException(
                $"Role name '{name.Trim()}' is reserved for platform use and cannot be used as a tenant role.");
        }

        var canonical = RoleNames.ResolveAssignable(name);
        if (canonical is not null)
            return CreateSystemAssignableForTenant(tenantId, canonical);

        return CreateCustomForTenant(tenantId, name);
    }

    public void EnsureCanBeRenamed()
    {
        if (IsSystem)
            throw new InvalidOperationException($"System role '{Name}' cannot be renamed.");
    }

    public void EnsureCanBeDeleted()
    {
        if (IsSystem)
            throw new InvalidOperationException($"System role '{Name}' cannot be deleted.");
    }

    public void EnsurePermissionsCanBeReplaced()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException(
                "System role permissions cannot be replaced via this API. Create a custom role instead.");
        }

        if (RoleNames.IsReservedRoleName(Name) || RoleNames.IsSuperAdmin(Name))
        {
            throw new InvalidOperationException(
                $"Permissions for role '{Name}' cannot be changed via this API.");
        }
    }

    /// <summary>
    /// Ensures this role may be assigned through the tenant role-assignment API as a custom role
    /// (not a system-seeded role such as legacy Caseworker).
    /// </summary>
    public void EnsureAssignableAsCustomRole()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException(
                $"System role '{Name}' cannot be assigned as a custom role. Create a custom role first.");
        }
    }

    public void Rename(string name)
    {
        EnsureCanBeRenamed();

        var normalized = NormalizeName(name);
        EnsureNameAllowedForCustomRole(normalized);
        Name = normalized;
    }

    /// <summary>
    /// Creates a permission grant belonging to this role (used for seeding and replace).
    /// </summary>
    public RolePermission CreatePermission(
        string resourceKey,
        ResourceType resourceType,
        AccessType accessType,
        DateTime createdOn)
    {
        if (Id is null)
            throw new InvalidOperationException("Role must have an Id before permissions can be added.");

        RolePermissionGrantRules.EnsureValid(resourceType, resourceKey, accessType);

        var permission = new RolePermission(
            new RolePermissionId(Guid.NewGuid()),
            Id,
            resourceKey.Trim(),
            resourceType,
            accessType,
            createdOn);

        _permissions.Add(permission);
        return permission;
    }

    /// <summary>
    /// Replaces all permission grants on this role. Callers must load the role with
    /// <see cref="Permissions"/> included so EF change-tracking deletes prior rows.
    /// </summary>
    public IReadOnlyList<RolePermission> BuildReplacedPermissions(
        IEnumerable<(ResourceType ResourceType, string ResourceKey, AccessType AccessType)> grants,
        DateTime when)
    {
        EnsurePermissionsCanBeReplaced();
        _permissions.Clear();

        var created = new List<RolePermission>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in grants ?? Array.Empty<(ResourceType, string, AccessType)>())
        {
            var key = grant.ResourceKey?.Trim() ?? string.Empty;
            var dedupeKey = $"{grant.ResourceType}:{key}:{grant.AccessType}";
            if (!seen.Add(dedupeKey))
                continue;

            created.Add(CreatePermission(
                grant.ResourceKey!,
                grant.ResourceType,
                grant.AccessType,
                when));
        }

        return created;
    }

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? throw new ArgumentNullException(nameof(name));
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Role name is required.", nameof(name));
        return normalized;
    }

    private static void EnsureNameAllowedForCustomRole(string name)
    {
        if (RoleNames.IsReservedRoleName(name) || RoleNames.IsAssignable(name))
        {
            throw new InvalidOperationException(
                $"Role name '{name}' is reserved or is a system role and cannot be used.");
        }
    }
}
