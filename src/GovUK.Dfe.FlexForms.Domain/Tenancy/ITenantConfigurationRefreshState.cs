namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Exposes when the in-memory tenant catalogue was last refreshed (database provider only).
/// </summary>
public interface ITenantConfigurationRefreshState
{
    DateTimeOffset? LastRefreshedUtc { get; }

    int ActiveTenantCount { get; }
}
