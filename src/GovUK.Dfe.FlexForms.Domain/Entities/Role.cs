using GovUK.Dfe.FlexForms.Domain.Common;
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
        Name = name?.Trim() ?? throw new ArgumentNullException(nameof(name));
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Role name is required.", nameof(name));
        TenantId = tenantId;
        IsSystem = isSystem;
    }

    /// <summary>
    /// Factory for a tenant-scoped role (system or custom).
    /// </summary>
    public static Role CreateForTenant(Guid tenantId, string name, bool isSystem)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new Role(new RoleId(Guid.NewGuid()), name, tenantId, isSystem);
    }

    public void Rename(string name)
    {
        if (IsSystem)
            throw new InvalidOperationException($"System role '{Name}' cannot be renamed.");

        Name = name?.Trim() ?? throw new ArgumentNullException(nameof(name));
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Role name is required.", nameof(name));
    }
}
