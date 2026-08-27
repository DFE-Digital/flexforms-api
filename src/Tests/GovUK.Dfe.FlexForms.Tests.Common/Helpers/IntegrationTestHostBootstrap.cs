namespace GovUK.Dfe.FlexForms.Tests.Common.Helpers;

/// <summary>
/// Environment variables Program.cs needs before the WebApplicationFactory can replace
/// <c>ITenantConfigurationProvider</c>. TenantConfigSource defaults to Database, and the
/// in-memory test database is empty at host build, which used to throw
/// "At least one tenant must be configured."
/// </summary>
public static class IntegrationTestHostBootstrap
{
    public const string TestTenantId = "11111111-1111-4111-8111-111111111111";

    public static void Apply()
    {
        Environment.SetEnvironmentVariable("TenantConfigSource", "AppSettings");
        Environment.SetEnvironmentVariable("Tenants__Transfers__Id", TestTenantId);
        Environment.SetEnvironmentVariable("Tenants__Transfers__Name", "Transfers");
        Environment.SetEnvironmentVariable("Tenants__Transfers__Frontend__Origin", "https://localhost:7020");

        // Host DI prefers GlobalConfiguration:FileStorage (required outside Local/Development).
        Environment.SetEnvironmentVariable("GlobalConfiguration__FileStorage__Provider", "Local");
        Environment.SetEnvironmentVariable("GlobalConfiguration__FileStorage__Local__BaseDirectory", "/uploads");
        Environment.SetEnvironmentVariable("GlobalConfiguration__FileStorage__Local__AllowedExtensions__0", "jpg");
        Environment.SetEnvironmentVariable("GlobalConfiguration__FileStorage__Local__AllowedExtensions__1", "png");
        Environment.SetEnvironmentVariable("GlobalConfiguration__FileStorage__Local__AllowedExtensions__2", "pdf");
        Environment.SetEnvironmentVariable("GlobalConfiguration__FileStorage__Local__AllowedExtensions__3", "docx");
        Environment.SetEnvironmentVariable("GlobalConfiguration__FileStorage__Local__AllowedExtensions__4", "xlsx");
        Environment.SetEnvironmentVariable("GlobalConfiguration__Email__Provider", "GovUkNotify");
        Environment.SetEnvironmentVariable("GlobalConfiguration__Email__GovUkNotify__ApiKey", "test-notify-key");
        Environment.SetEnvironmentVariable("GlobalConfiguration__Email__ServiceSupportEmailAddress", "some.email@education.gov.uk");

        // Tenant FileStorage/Email must also be present at runtime (no host fallback).
        // See TestTenantConfigurationProvider.
        Environment.SetEnvironmentVariable("Tenants__Transfers__FileStorage__Provider", "Local");
        Environment.SetEnvironmentVariable("Tenants__Transfers__FileStorage__Local__BaseDirectory", "/uploads");
        Environment.SetEnvironmentVariable("Tenants__Transfers__FileStorage__Local__AllowedExtensions__0", "jpg");
        Environment.SetEnvironmentVariable("Tenants__Transfers__FileStorage__Local__AllowedExtensions__1", "png");
        Environment.SetEnvironmentVariable("Tenants__Transfers__FileStorage__Local__AllowedExtensions__2", "pdf");
        Environment.SetEnvironmentVariable("Tenants__Transfers__FileStorage__Local__AllowedExtensions__3", "docx");
        Environment.SetEnvironmentVariable("Tenants__Transfers__FileStorage__Local__AllowedExtensions__4", "xlsx");
        Environment.SetEnvironmentVariable("Tenants__Transfers__Email__Provider", "GovUkNotify");
        Environment.SetEnvironmentVariable("Tenants__Transfers__Email__GovUkNotify__ApiKey", "test-notify-key");
        Environment.SetEnvironmentVariable("Tenants__Transfers__Email__ServiceSupportEmailAddress", "some.email@education.gov.uk");
        Environment.SetEnvironmentVariable("Tenants__Transfers__ConnectionStrings__DefaultConnection",
            "Server=localhost,1433;Database=ExternalApplications;User Id=SA;Password=YourPassword123!;TrustServerCertificate=True;");
        Environment.SetEnvironmentVariable("SkipMassTransit", "true");
        Environment.SetEnvironmentVariable("SkipBackgroundService", "true");
        Environment.SetEnvironmentVariable("Tenants__Transfers__NotificationService__RedisConnectionString", "localhost:6379");
    }
}
