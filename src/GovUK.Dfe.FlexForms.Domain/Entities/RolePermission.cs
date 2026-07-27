using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

/// <summary>
/// Permission grant attached to a tenant-scoped <see cref="Role"/>.
/// Phase-2 surface: tenants will edit these rows to reshape what each role can do.
/// </summary>
public sealed class RolePermission : BaseAggregateRoot, IEntity<RolePermissionId>
{
    public RolePermissionId? Id { get; private set; }
    public RoleId RoleId { get; private set; }
    public Role? Role { get; private set; }
    public string ResourceKey { get; private set; } = null!;
    public ResourceType ResourceType { get; private set; }
    public AccessType AccessType { get; private set; }
    public DateTime CreatedOn { get; private set; }

    private RolePermission()
    {
    }

    public RolePermission(
        RolePermissionId id,
        RoleId roleId,
        string resourceKey,
        ResourceType resourceType,
        AccessType accessType,
        DateTime createdOn)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        RoleId = roleId ?? throw new ArgumentNullException(nameof(roleId));
        ResourceKey = string.IsNullOrWhiteSpace(resourceKey)
            ? throw new ArgumentException("ResourceKey is required.", nameof(resourceKey))
            : resourceKey.Trim();
        ResourceType = resourceType;
        AccessType = accessType;
        CreatedOn = createdOn;
    }
}
