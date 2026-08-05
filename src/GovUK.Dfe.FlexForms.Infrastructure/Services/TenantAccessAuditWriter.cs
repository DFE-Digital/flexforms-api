using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

public sealed class TenantAccessAuditWriter(
    ExternalApplicationsContext dbContext,
    ILogger<TenantAccessAuditWriter> logger) : ITenantAccessAuditWriter
{
    public async Task AppendAsync(
        Guid tenantId,
        UserId? subjectUserId,
        string subjectEmail,
        string action,
        string? roleName,
        UserId? actorUserId,
        string actorEmail,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var entry = TenantAccessAudit.Create(
            tenantId,
            subjectUserId,
            subjectEmail,
            action,
            roleName,
            actorUserId,
            actorEmail,
            details);

        dbContext.TenantAccessAudits.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Tenant access audit: {Action} role={Role} subject={Subject} by {Actor} (tenant {TenantId})",
            action,
            roleName,
            subjectEmail,
            actorEmail,
            tenantId);
    }
}
