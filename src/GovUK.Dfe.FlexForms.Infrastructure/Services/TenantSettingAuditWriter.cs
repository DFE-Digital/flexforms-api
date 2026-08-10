using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

public sealed class TenantSettingAuditWriter(
    TenantConfigDbContext dbContext,
    ILogger<TenantSettingAuditWriter> logger) : ITenantSettingAuditWriter
{
    public async Task AppendAsync(
        Guid tenantId,
        string category,
        string target,
        string action,
        string actorEmail,
        bool wasSecret,
        CancellationToken cancellationToken = default)
    {
        var entry = new TenantSettingAuditEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Category = category,
            Target = target,
            Action = action,
            ActorEmail = actorEmail,
            ChangedAtUtc = DateTime.UtcNow,
            WasSecret = wasSecret
        };

        dbContext.TenantSettingAudits.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Tenant setting audit: {Action} {Category}/{Target} for tenant {TenantId} by {Actor}",
            action,
            category,
            target,
            tenantId,
            actorEmail);
    }
}
