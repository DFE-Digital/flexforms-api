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

    /// <summary>
    /// Creates a new active membership binding a user to a tenant role.
    /// </summary>
    public static TenantMembership Create(
        Guid tenantId,
        UserId userId,
        RoleId roleId,
        DateTime createdOn)
    {
        return new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            tenantId,
            userId,
            roleId,
            createdOn,
            isActive: true);
    }

    /// <summary>
    /// Creates a self-registered tenant membership with the standard User role.
    /// Elevation to Admin/custom roles is done via administrative assignment.
    /// </summary>
    public static TenantMembership CreateSelfRegisteredUser(
        Guid tenantId,
        UserId userId,
        RoleId userRoleId,
        DateTime createdOn)
    {
        if (userRoleId is null)
            throw new ArgumentNullException(nameof(userRoleId));

        return Create(tenantId, userId, userRoleId, createdOn);
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

    /// <summary>
    /// Reassigns the membership role and ensures the membership is active.
    /// </summary>
    public void ReassignAndActivate(RoleId roleId, DateTime when)
    {
        AssignRole(roleId, when);
        Activate(when);
    }
}
