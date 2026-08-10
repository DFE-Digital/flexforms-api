namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

public interface ITenantSettingAuditWriter
{
    Task AppendAsync(
        Guid tenantId,
        string category,
        string target,
        string action,
        string actorEmail,
        bool wasSecret,
        CancellationToken cancellationToken = default);
}
