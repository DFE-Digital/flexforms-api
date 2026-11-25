using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

/// <summary>
/// Tenant settings administration over HTTP, with the real policy, permission checks, tenant context and an
/// in-memory TenantConfig store. Handler branches are covered in Application.Tests; these pin how the pieces fit.
/// </summary>
public class TenantAdminControllerSettingsTests
{
    private const string AdminEmail = "alice@example.com";
    private const string StoredSecret = "stored-client-secret";
    private static readonly Guid TenantId = Guid.Parse(EaContextSeeder.TestTenantId);
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    // ---- delegated (safe) organisation settings ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SafeSettings_AdminSavesAndReadsBack_AndTheChangeIsAudited(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, Admin());

        var saved = await client.UpsertSafeTenantSettingAsync(TenantId, Setting("Dashboard", """{"PageSize":25}"""));
        var settings = await client.GetSafeTenantSettingsAsync(TenantId);
        var audit = await client.GetSettingAuditLogAsync(TenantId);

        Assert.True(saved.WasCreated);
        Assert.Equal("Web", saved.Target);
        Assert.Contains(settings.Settings, s => s.Category == "Dashboard" && s.SettingsJson.Contains("25"));
        Assert.Contains(audit.Entries, e => e.Category == "Dashboard" && e.Action == "Created" && e.ActorEmail == AdminEmail);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SafeSettings_ApiReadCategoryIsStoredUnderShared(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, Admin());

        var saved = await client.UpsertSafeTenantSettingAsync(TenantId, Setting("FileValidation", """{"Mode":"Strict"}"""));

        Assert.Equal("Shared", saved.Target);
        var targets = await WithTenantConfigAsync(factory, db => db.TenantSettings
            .Where(s => s.TenantId == TenantId && s.Category == "FileValidation")
            .Select(s => s.Target)
            .ToListAsync());
        Assert.Equal(["Shared"], targets);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SafeSettings_ServicePrincipalWithAnAdminRoleIsRefused(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, [.. Admin(), new(TenantAuthClaimTypes.IsService, "true")]);

        var get = await ApiErrorAsync(() => client.GetSafeTenantSettingsAsync(TenantId));
        var put = await ApiErrorAsync(() => client.UpsertSafeTenantSettingAsync(TenantId, Setting("Dashboard", "{}")));

        Assert.Equal(403, get.Status);
        Assert.Equal(403, put.Status);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SafeSettings_AnotherTenantIsRefused(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, SuperAdmin());

        var get = await ApiErrorAsync(() => client.GetSafeTenantSettingsAsync(OtherTenantId));
        var put = await ApiErrorAsync(() => client.UpsertSafeTenantSettingAsync(OtherTenantId, Setting("Dashboard", "{}")));

        Assert.Equal(403, get.Status);
        Assert.Equal(403, put.Status);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SafeSettings_BadRequestsAreRefusedWithoutSaving(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, Admin());

        var unsafeCategory = await ApiErrorAsync(() => client.UpsertSafeTenantSettingAsync(TenantId, Setting("ConnectionStrings", "{}")));
        var notBase64 = await ApiErrorAsync(() => client.UpsertSafeTenantSettingAsync(
            TenantId, new UpsertTenantSettingRequest("Dashboard", "Web", "not base64!", false)));
        var empty = await ApiErrorAsync(() => client.UpsertSafeTenantSettingAsync(TenantId, Setting("Dashboard", " ")));
        var invalid = await ApiErrorAsync(() => client.UpsertSafeTenantSettingAsync(TenantId, Setting("Dashboard", """{"PageSize":0}""")));

        Assert.Equal(400, unsafeCategory.Status);
        Assert.Contains("is not a delegated organisation setting", unsafeCategory.Message);
        Assert.Equal(400, notBase64.Status);
        Assert.Equal(400, empty.Status);
        Assert.Equal(400, invalid.Status);
        Assert.Equal(0, await WithTenantConfigAsync(factory, db => db.TenantSettings.CountAsync()));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SafeSettings_TenantMissingFromTheStoreIsNotFound(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());

        var get = await ApiErrorAsync(() => client.GetSafeTenantSettingsAsync(TenantId));
        var put = await ApiErrorAsync(() => client.UpsertSafeTenantSettingAsync(TenantId, Setting("Dashboard", "{}")));

        Assert.Equal(404, get.Status);
        Assert.Equal(404, put.Status);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SafeSettings_HidesEverythingElse(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, SuperAdmin());
        await client.UpsertTenantSettingAsync(TenantId, Setting("Layout", """{"Theme":"govuk"}"""));
        await client.UpsertTenantSettingAsync(TenantId, Setting("Dashboard", """{"PageSize":10}"""));
        CallAs(factory, httpClient, Admin());

        var settings = await client.GetSafeTenantSettingsAsync(TenantId);

        Assert.Equal(["Dashboard"], settings.Settings.Select(s => s.Category));
    }

    // ---- full settings: the categories only a SuperAdmin may touch ----
    // Only the six categories restricted today are asserted here.

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Settings_SuperAdminOnlyCategoriesAreRefusedForATenantAdmin(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, Admin());

        foreach (var category in SuperAdminOnlyTenantSettingCategories.All)
        {
            var upsert = await ApiErrorAsync(() => client.UpsertTenantSettingAsync(TenantId, Setting(category, "{}", "Shared")));
            var delete = await ApiErrorAsync(() => client.DeleteTenantSettingAsync(TenantId, category, "Shared"));
            var validate = await ApiErrorAsync(() => client.ValidateTenantSettingAsync(
                TenantId, new ValidateTenantSettingRequest(category, "Shared", ToBase64("{}"), false)));

            Assert.Equal(403, upsert.Status);
            Assert.Equal(403, delete.Status);
            Assert.Equal(403, validate.Status);
        }

        Assert.Equal(0, await WithTenantConfigAsync(factory, db => db.TenantSettings.CountAsync()));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Validate_RedactsAStoredSecretForATenantAdmin(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        var json = $$"""{"ClientId":"app","ClientSecret":"{{StoredSecret}}"}""";
        CallAs(factory, httpClient, SuperAdmin());
        await client.UpsertTenantSettingAsync(TenantId, Setting("Layout", json, isSecret: true));
        var request = new ValidateTenantSettingRequest("Layout", "Web", ToBase64(json), true);

        // The test host runs as "Local", one of the environments where a SuperAdmin may see secrets.
        var asSuperAdmin = await client.ValidateTenantSettingAsync(TenantId, request);
        CallAs(factory, httpClient, Admin());
        var asAdmin = await client.ValidateTenantSettingAsync(TenantId, request);

        Assert.Contains(StoredSecret, asSuperAdmin.CurrentSettingsJson);
        Assert.True(asAdmin.SettingExists);
        Assert.DoesNotContain(StoredSecret, asAdmin.CurrentSettingsJson);
        Assert.DoesNotContain(StoredSecret, asAdmin.ProposedSettingsJson);
    }

    // ---- export and import ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task ExportAndImport_AreRefusedForATenantAdmin(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, Admin());

        var export = await ApiErrorAsync(() => client.ExportConfigurationAsync(TenantId));
        var import = await ApiErrorAsync(() => client.ImportConfigurationAsync(
            TenantId, new ImportTenantConfigurationDto([new("Dashboard", "Web", "{}", false)], true)));

        Assert.Equal(403, export.Status);
        Assert.Equal(403, import.Status);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task ExportAndImport_SuperAdminRoundTripsWithoutOverwritingSecrets(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, SuperAdmin());
        await client.UpsertTenantSettingAsync(TenantId, Setting("Layout", $$"""{"ClientSecret":"{{StoredSecret}}"}""", isSecret: true));
        await client.UpsertTenantSettingAsync(TenantId, Setting("Dashboard", """{"PageSize":10}"""));

        var export = await client.ExportConfigurationAsync(TenantId);
        var bundle = new ImportTenantConfigurationDto(
            export.Settings.Select(s => new TenantSettingImportItemDto(
                s.Category, s.Target, s.Category == "Dashboard" ? """{"PageSize":20}""" : s.SettingsJson, s.IsSecret)).ToList(),
            SkipSecretPlaceholders: true);
        var imported = await client.ImportConfigurationAsync(TenantId, bundle);
        var validate = await client.ValidateTenantSettingAsync(
            TenantId, new ValidateTenantSettingRequest("Layout", "Web", ToBase64("{}"), true));

        var secret = Assert.Single(export.Settings, s => s.Category == "Layout");
        Assert.True(secret.SecretRedacted);
        Assert.DoesNotContain(StoredSecret, secret.SettingsJson);
        Assert.Equal(1, imported.AppliedCount);
        Assert.Equal(1, imported.SkippedCount);
        Assert.Contains(StoredSecret, validate.CurrentSettingsJson);
        var dashboard = await client.GetSafeTenantSettingsAsync(TenantId);
        Assert.Contains("20", Assert.Single(dashboard.Settings).SettingsJson);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task ExportAndImport_AnotherTenantOrAnEmptyBundleIsRefused(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, SuperAdmin());

        var otherExport = await ApiErrorAsync(() => client.ExportConfigurationAsync(OtherTenantId));
        var otherImport = await ApiErrorAsync(() => client.ImportConfigurationAsync(
            OtherTenantId, new ImportTenantConfigurationDto([new("Dashboard", "Web", "{}", false)], true)));
        var empty = await ApiErrorAsync(() => client.ImportConfigurationAsync(TenantId, new ImportTenantConfigurationDto([], true)));

        Assert.Equal(403, otherExport.Status);
        Assert.Equal(403, otherImport.Status);
        Assert.Equal(400, empty.Status);
        Assert.Contains("At least one setting is required.", empty.Message);
    }

    // ---- effective configuration, platform tenants and the audit log ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task EffectiveConfig_TenantAdminSeesNoPlatformDetails(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, Admin());

        var config = await client.GetEffectiveConfigurationAsync(TenantId);
        var other = await ApiErrorAsync(() => client.GetEffectiveConfigurationAsync(OtherTenantId));

        Assert.Equal(TenantId, config.TenantId);
        Assert.Equal(0, config.ActiveTenantCount);
        Assert.Equal(0, config.RegisteredAuthProviderCount);
        Assert.Empty(config.FrontendOrigins);
        Assert.Equal(403, other.Status);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task PlatformTenants_OnlyASuperAdminCanListThem(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var asAdmin = await ApiErrorAsync(() => client.GetPlatformTenantsAsync());
        CallAs(factory, httpClient, SuperAdmin());
        var asSuperAdmin = await client.GetPlatformTenantsAsync();

        Assert.Equal(403, asAdmin.Status);
        Assert.Contains(asSuperAdmin.Tenants, t => t.TenantId == TenantId);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AuditLog_IsRefusedForAnotherTenantOrAServicePrincipal(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, Admin());
        var otherTenant = await ApiErrorAsync(() => client.GetSettingAuditLogAsync(OtherTenantId));
        CallAs(factory, httpClient, [.. Admin(), new(TenantAuthClaimTypes.IsService, "true")]);
        var service = await ApiErrorAsync(() => client.GetSettingAuditLogAsync(TenantId));

        Assert.Equal(403, otherTenant.Status);
        Assert.Equal(403, service.Status);
    }

    private static UpsertTenantSettingRequest Setting(string category, string json, string target = "Web", bool isSecret = false) =>
        new(category, target, ToBase64(json), isSecret);

    private static string ToBase64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static async Task SeedTenantAsync(CustomWebApplicationDbContextFactory<Program> factory) =>
        await WithTenantConfigAsync(factory, async db =>
        {
            db.Tenants.Add(new TenantEntity { Id = TenantId, Name = "Transfers" });
            return await db.SaveChangesAsync();
        });

    private static async Task<T> WithTenantConfigAsync<T>(
        CustomWebApplicationDbContextFactory<Program> factory, Func<TenantConfigDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TenantConfigDbContext>());
    }

    private static async Task<(int Status, string Message)> ApiErrorAsync(Func<Task> call)
    {
        var ex = await Assert.ThrowsAnyAsync<ExternalApplicationsException>(call);
        var message = ex is ExternalApplicationsException<ExceptionResponse> typed
            ? typed.Result?.Message ?? ""
            : ex.Response ?? "";
        return (ex.StatusCode, message);
    }

    private static List<Claim> Admin() => [new(ClaimTypes.Email, AdminEmail), new(ClaimTypes.Role, RoleNames.Admin)];

    private static List<Claim> SuperAdmin() => [new(ClaimTypes.Email, "root@example.com"), new(ClaimTypes.Role, RoleNames.SuperAdmin)];

    private static void CallAs(CustomWebApplicationDbContextFactory<Program> factory, HttpClient httpClient, List<Claim> claims)
    {
        // CoreLibs reads TestClaims once, when the mock handler is first built; change the same list.
        factory.TestClaims ??= [];
        factory.TestClaims.Clear();
        factory.TestClaims.AddRange(claims);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user-token");
    }
}
