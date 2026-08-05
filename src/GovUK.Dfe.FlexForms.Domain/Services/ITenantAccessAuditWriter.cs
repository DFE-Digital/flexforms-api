using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services;

public interface ITenantAccessAuditWriter
{
    Task AppendAsync(
        Guid tenantId,
        UserId? subjectUserId,
        string subjectEmail,
        string action,
        string? roleName,
        UserId? actorUserId,
        string actorEmail,
        string? details = null,
        CancellationToken cancellationToken = default);
}
