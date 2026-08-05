namespace GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;

/// <summary>
/// Audit trail for TenantConfig setting changes (SuperAdmin upserts).
/// </summary>
public class TenantSettingAuditEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    /// <summary>Created or Updated.</summary>
    public string Action { get; set; } = string.Empty;

    public string ActorEmail { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; }

    public bool WasSecret { get; set; }

    public TenantEntity? Tenant { get; set; }
}
