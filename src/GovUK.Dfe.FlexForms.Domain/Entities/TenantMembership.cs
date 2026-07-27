using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

/// <summary>
/// Binds a user to a single tenant with a tenant-scoped role.
/// In a shared EA database this is the source of truth for "is this person Admin of LSRP?"
/// (as opposed to a global <see cref="User.RoleId"/>).
/// </summary>
public sealed class TenantMembership : BaseAggregateRoot, IEntity<TenantMembershipId>
{
    public TenantMembershipId? Id { get; private set; }
    public Guid TenantId { get; private set; }
    public UserId UserId { get; private set; }
    public User? User { get; private set; }
    public RoleId RoleId { get; private set; }
    public Role? Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedOn { get; private set; }
    public DateTime? LastModifiedOn { get; private set; }

    private TenantMembership()
    {
    }

    public TenantMembership(
        TenantMembershipId id,
        Guid tenantId,
        UserId userId,
        RoleId roleId,
        DateTime createdOn,
        bool isActive = true)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        Id = id ?? throw new ArgumentNullException(nameof(id));
        TenantId = tenantId;
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
        RoleId = roleId ?? throw new ArgumentNullException(nameof(roleId));
        CreatedOn = createdOn;
        IsActive = isActive;
    }

    public void AssignRole(RoleId roleId, DateTime when)
    {
        RoleId = roleId ?? throw new ArgumentNullException(nameof(roleId));
        LastModifiedOn = when;
    }

    public void Activate(DateTime when)
    {
        IsActive = true;
        LastModifiedOn = when;
    }

    public void Deactivate(DateTime when)
    {
        IsActive = false;
        LastModifiedOn = when;
    }
}
