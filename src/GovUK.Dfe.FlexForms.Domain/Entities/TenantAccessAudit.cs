using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

/// <summary>
/// Audit trail for tenant user/role changes (e.g. who granted Admin access).
/// </summary>
public class TenantAccessAudit
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? SubjectUserId { get; set; }

    public string SubjectEmail { get; set; } = string.Empty;

    /// <summary>e.g. RoleAssigned, RoleRemoved, MembershipDeactivated.</summary>
    public string Action { get; set; } = string.Empty;

    public string? RoleName { get; set; }

    public Guid? ActorUserId { get; set; }

    public string ActorEmail { get; set; } = string.Empty;

    public string? Details { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public static TenantAccessAudit Create(
        Guid tenantId,
        UserId? subjectUserId,
        string subjectEmail,
        string action,
        string? roleName,
        UserId? actorUserId,
        string actorEmail,
        string? details = null,
        DateTime? occurredAtUtc = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubjectUserId = subjectUserId?.Value,
            SubjectEmail = subjectEmail.Trim(),
            Action = action.Trim(),
            RoleName = string.IsNullOrWhiteSpace(roleName) ? null : roleName.Trim(),
            ActorUserId = actorUserId?.Value,
            ActorEmail = actorEmail.Trim(),
            Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow
        };
}
