using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Tests.Common.Helpers;

/// <summary>
/// Provides tenant configuration from the customization (in-memory) so integration tests
/// do not depend on appsettings. Use for tests that need tenant-specific settings (e.g. FileStorage:Local).
/// </summary>
public sealed class TestTenantConfigurationProvider : ITenantConfigurationProvider
{
    private readonly IReadOnlyCollection<TenantConfiguration> _tenants;

    public TestTenantConfigurationProvider(string testTenantId, string tenantName = "Transfers")
    {
        var tenantId = Guid.Parse(testTenantId);
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ApplicationReference:Prefix", "TRF" },
                { "ApplicationTemplates:HostMappings:Transfers", EaContextSeeder.TemplateId },
                { "FileStorage:Local:BaseDirectory", "/uploads" },
                { "FileStorage:Local:AllowedExtensions:0", "jpg" },
                { "FileStorage:Local:AllowedExtensions:1", "png" },
                { "FileStorage:Local:AllowedExtensions:2", "pdf" },
                { "FileStorage:Local:AllowedExtensions:3", "docx" },
                { "FileStorage:Local:AllowedExtensions:4", "xlsx" },
                { "FrontendSettings:BaseUrl", "https://test.apply.example.gov.uk" },
                { "FrontendSettings:PreviewContentSelector", ".govuk-grid-column-full" },
                { "InternalServiceAuth:Services:0:Email", "test-service@service.com" },
                { "InternalServiceAuth:Services:0:ApiKey", "secret" },
                { "Email:ServiceSupportEmailAddress", "some.email@education.gov.uk" },
                // Authorization:TokenSettings here so the per-tenant named TokenSettings registered by
                // AddCustomAuthorization matches the integration test signing config.
                { "Authorization:TokenSettings:SecretKey", "iw5/ivfUWaCpj+n3TihlGUzRVna+KKu8IfLP52GdgNXlDcqt3+N2MM45rwQ=" },
                { "Authorization:TokenSettings:Issuer", "21f3ed37-8443-4755-9ed2-c68ca86b4398" },
                { "Authorization:TokenSettings:Audience", "20dafd6d-79e5-4caf-8b72-d070dcc9716f" },
                { "Authorization:TokenSettings:TokenLifetimeMinutes", "60" },
                { "ConnectionStrings:Redis", "localhost:6379" },
                { "NotificationService:RedisConnectionString", "localhost:6379" },
            })
            .Build();
        var tenant = new TenantConfiguration(
            tenantId,
            tenantName,
            settings,
            new[] { "https://localhost:7020" });
        _tenants = new[] { tenant };
    }

    public string Source => "Test";

    public TenantConfiguration? GetTenant(Guid id)
        => _tenants.FirstOrDefault(t => t.Id == id);

    public TenantConfiguration? GetTenantByOrigin(string origin)
        => _tenants.FirstOrDefault(t => t.FrontendOrigins.Any(
            o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyCollection<TenantConfiguration> GetAllTenants()
        => _tenants;
}
