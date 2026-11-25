using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Tests.Common.Helpers;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;

/// <summary>How the two tenants' user tokens are signed.</summary>
public enum TenantSigning
{
    /// <summary>Each tenant has its own key, issuer and audience, as in production.</summary>
    Distinct,

    /// <summary>
    /// Both tenants share one key, issuer and audience, so a tenant A token is cryptographically valid
    /// for tenant B and its <c>tenant_id</c> claim is the only thing that stops it.
    /// </summary>
    Shared,
}

/// <summary>
/// One tenant in the two-tenant host: its configuration, the service API key Web would use to call
/// token exchange, and the ids of everything <see cref="TwoTenantSeeder"/> creates for it.
/// </summary>
public sealed class TestTenant
{
    private readonly char _idChar;

    private TestTenant(char idChar, string key, string name)
    {
        _idChar = idChar;
        Key = key;
        Name = name;
    }

    public static TestTenant A { get; } = new('a', "alpha", "Alpha");

    public static TestTenant B { get; } = new('b', "beta", "Beta");

    /// <summary>Lower-case short name, used in emails and API keys.</summary>
    public string Key { get; }

    public string Name { get; }

    public Guid Id => Ids(0);

    public string Origin => $"https://{Key}.example";

    /// <summary>The raw service API key (a service principal provider) Web would send to token exchange.</summary>
    public string ServiceApiKey => $"{Key}-web-service-api-key";

    public Guid AdminRoleId => Ids(10);
    public Guid UserRoleId => Ids(11);
    public Guid CaseworkerRoleId => Ids(12);
    public Guid TemplateManagerRoleId => Ids(13);
    public Guid UserManagerRoleId => Ids(14);

    public Guid OwnerId => Ids(20);
    public Guid AdminId => Ids(21);
    public Guid CaseworkerId => Ids(22);
    public Guid TemplateManagerId => Ids(23);
    public Guid UserManagerId => Ids(24);

    public Guid TemplateId => Ids(30);
    public Guid TemplateVersionId => Ids(31);
    public Guid ApplicationId => Ids(40);
    public string ApplicationReference => $"{Key.ToUpperInvariant()[..3]}-0001";
    public Guid ResponseId => Ids(41);
    public Guid FileId => Ids(42);

    /// <summary>Created the tenant's application and holds its application and file grants.</summary>
    public string OwnerEmail => $"owner@{Key}.example";
    public string AdminEmail => $"admin@{Key}.example";
    public string CaseworkerEmail => $"caseworker@{Key}.example";
    public string TemplateManagerEmail => $"template.manager@{Key}.example";
    public string UserManagerEmail => $"user.manager@{Key}.example";

    public IEnumerable<string> MemberEmails =>
        [OwnerEmail, AdminEmail, CaseworkerEmail, TemplateManagerEmail, UserManagerEmail];

    public TenantConfiguration ToConfiguration(TenantSigning signing)
    {
        var settings = TestTenantConfigurationProvider.DefaultSettings();
        settings.Remove("ApplicationTemplates:HostMappings:Transfers");
        settings[$"ApplicationTemplates:HostMappings:{Name}"] = TemplateId.ToString();
        settings["ApplicationReference:Prefix"] = Key.ToUpperInvariant()[..3];
        settings["InternalServiceAuth:Services:0:ApiKey"] = $"{Key}-internal-service-key";

        var tokenKey = signing == TenantSigning.Shared ? "shared" : Key;
        settings["Authorization:TokenSettings:SecretKey"] = $"{tokenKey}-user-token-signing-key-0123456789abcdef";
        settings["Authorization:TokenSettings:Issuer"] = $"flexforms-{tokenKey}";
        settings["Authorization:TokenSettings:Audience"] = $"flexforms-{tokenKey}-api";

        settings["AuthProviders:Providers:0:Kind"] = nameof(TenantAuthProviderKind.ApiKey);
        settings["AuthProviders:Providers:0:Name"] = "web-service";
        settings["AuthProviders:Providers:0:IsServicePrincipal"] = "true";
        settings["AuthProviders:Providers:0:KeyHash"] = TenantApiKeyHasher.Hash(ServiceApiKey);

        return new TenantConfiguration(
            Id,
            Name,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            [Origin]);
    }

    public override string ToString() => Name;

    private Guid Ids(int n) => Guid.Parse($"{new string(_idChar, 8)}-0000-4000-8000-{n:D12}");
}
