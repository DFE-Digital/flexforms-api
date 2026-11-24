using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Tests.Common.Helpers;

/// <summary>
/// The API registers <see cref="ITenantConfigurationRefreshState"/> only when tenants come from the database.
/// The test host loads them from <see cref="TestTenantConfigurationProvider"/>, so the admin endpoints that
/// report refresh state need this stand-in.
/// </summary>
public sealed class TestTenantConfigurationRefreshState : ITenantConfigurationRefreshState
{
    public static readonly DateTimeOffset RefreshedAtUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset? LastRefreshedUtc => RefreshedAtUtc;

    public int ActiveTenantCount => 1;
}
