using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using Microsoft.EntityFrameworkCore;
using static GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers.TenantAdminHttpTestKit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

/// <summary>
/// The rest of <c>/v1/admin/tenants</c> over HTTP: tenant summaries and refresh, full settings and the
/// secret reveal, creating a tenant, health, and the cookbook and event catalogue. Handler branches are
/// covered in Application.Tests; these pin the policy, the tenant check and the status codes.
/// </summary>
public class TenantAdminControllerTenantTests
{
    private const string StoredSecret = "stored-client-secret";

    // ---- tenant summaries and refresh ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Tenants_AdminSeesOnlyTheirOwnTenant(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());

        var tenants = await client.GetTenantsAsync();
        var refreshed = await client.RefreshTenantConfigurationAsync();

        var tenant = Assert.Single(tenants.Tenants);
        Assert.Equal(TenantId, tenant.Id);
        Assert.Equal(1, tenants.TenantCount);
        Assert.Equal("Test", tenants.Source);
        Assert.Equal(TenantId, Assert.Single(refreshed.Tenants).Id);
        Assert.Equal(1, refreshed.TenantCount);
        Assert.Equal("Tenant configuration refreshed successfully.", refreshed.Message);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Tenants_RefusedForAServicePrincipalOrAUserWithoutAnAdminRole(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        CallAs(factory, httpClient, [.. Admin(), new(TenantAuthClaimTypes.IsService, "true")]);
        var serviceList = await ApiErrorAsync(() => client.GetTenantsAsync());
        var serviceRefresh = await ApiErrorAsync(() => client.RefreshTenantConfigurationAsync());
        CallAs(factory, httpClient, [new(ClaimTypes.Email, AdminEmail)]);
        var userList = await ApiErrorAsync(() => client.GetTenantsAsync());
        var userRefresh = await ApiErrorAsync(() => client.RefreshTenantConfigurationAsync());

        Assert.Equal(403, serviceList.Status);
        Assert.Equal(403, serviceRefresh.Status);
        Assert.Equal(403, userList.Status);
        Assert.Equal(403, userRefresh.Status);
    }

    // ---- full settings and the secret reveal ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Settings_SecretsAreRedactedForATenantAdmin_AndAnotherTenantIsRefused(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);

        // The test host runs as "Local", one of the environments where a SuperAdmin may see secrets.
        var asSuperAdmin = await client.GetTenantSettingsAsync(TenantId);
        CallAs(factory, httpClient, Admin());
        var asAdmin = await client.GetTenantSettingsAsync(TenantId);
        var other = await ApiErrorAsync(() => client.GetTenantSettingsAsync(OtherTenantId));

        Assert.Contains(StoredSecret, Assert.Single(asSuperAdmin.Settings).SettingsJson);
        var redacted = Assert.Single(asAdmin.Settings);
        Assert.True(redacted.IsSecret);
        Assert.DoesNotContain(StoredSecret, redacted.SettingsJson);
        Assert.Equal(403, other.Status);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Settings_TenantMissingFromTheStoreIsNotFound(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        CallAs(factory, httpClient, SuperAdmin());

        var get = await ApiErrorAsync(() => client.GetTenantSettingsAsync(TenantId));
        var delete = await ApiErrorAsync(() => client.DeleteTenantSettingAsync(TenantId, "Layout", "Web"));

        Assert.Equal(404, get.Status);
        Assert.Equal(404, delete.Status);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Reveal_SuperAdminGetsOneSecretValue_AndTheRevealIsAudited(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);

        var revealed = await client.RevealTenantSettingSecretAsync(
            TenantId, new RevealTenantSettingSecretRequest("Layout", "Web", "Auth:ClientSecret", "Rotating the key in Entra"));
        var audit = await client.GetSettingAuditLogAsync(TenantId);

        Assert.Equal(StoredSecret, revealed.Value);
        Assert.Equal(StoredSecret.Length, revealed.ValueLength);
        Assert.Equal("root@example.com", revealed.RevealedBy);
        Assert.Contains(audit.Entries, e => e.WasSecret
            && e.ActorEmail == "root@example.com"
            && e.Details.Contains("Auth:ClientSecret")
            && e.Details.Contains("Rotating the key in Entra"));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Reveal_IsRefusedForATenantAdminOrAnotherTenant(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);
        var request = new RevealTenantSettingSecretRequest("Layout", "Web", "Auth:ClientSecret", "Rotating the key in Entra");

        var otherTenant = await ApiErrorAsync(() => client.RevealTenantSettingSecretAsync(OtherTenantId, request));
        CallAs(factory, httpClient, Admin());
        var asAdmin = await ApiErrorAsync(() => client.RevealTenantSettingSecretAsync(TenantId, request));

        Assert.Equal(403, otherTenant.Status);
        Assert.Equal(403, asAdmin.Status);
        Assert.DoesNotContain(StoredSecret, asAdmin.Message);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Reveal_BadRequestsAreRefused(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);
        await client.UpsertTenantSettingAsync(TenantId, Setting("Dashboard", """{"PageSize":10}"""));
        const string reason = "Rotating the key in Entra";

        var shortReason = await ApiErrorAsync(() => client.RevealTenantSettingSecretAsync(
            TenantId, new RevealTenantSettingSecretRequest("Layout", "Web", "Auth:ClientSecret", "because")));
        var notSecret = await ApiErrorAsync(() => client.RevealTenantSettingSecretAsync(
            TenantId, new RevealTenantSettingSecretRequest("Dashboard", "Web", "PageSize", reason)));
        var noSetting = await ApiErrorAsync(() => client.RevealTenantSettingSecretAsync(
            TenantId, new RevealTenantSettingSecretRequest("Layout", "Api", "Auth:ClientSecret", reason)));
        var noPath = await ApiErrorAsync(() => client.RevealTenantSettingSecretAsync(
            TenantId, new RevealTenantSettingSecretRequest("Layout", "Web", "Auth:Missing", reason)));

        Assert.Equal(400, shortReason.Status);
        Assert.Equal(400, notSecret.Status);
        Assert.Equal(404, noSetting.Status);
        Assert.Equal(404, noPath.Status);
    }

    // ---- creating a tenant ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Clone_SuperAdminCreatesTheTenant_WithOnlyTheSubmittedSettings(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);
        var newTenantId = Guid.NewGuid();

        var created = await client.CloneTenantAsync(
            TenantId, new CloneTenantRequest(newTenantId, "Visits", NewTenantPayload("visits.example", "https://visits.example")));

        Assert.Equal(TenantId, created.SourceTenantId);
        Assert.Equal(newTenantId, created.NewTenantId);
        Assert.Equal("visits.example", created.Hostname);
        Assert.Equal(NewTenantSettings.Length, created.SettingsCopied);
        var stored = await WithTenantConfigAsync(factory, db => db.TenantSettings
            .Where(s => s.TenantId == newTenantId)
            .Select(s => s.Category)
            .ToListAsync());
        Assert.Equal(NewTenantSettings.Select(s => s.Category).Order(), stored.Order());
        Assert.True(await WithTenantConfigAsync(factory, db => db.TenantHostnames
            .AnyAsync(h => h.TenantId == newTenantId && h.Hostname == "visits.example")));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Clone_LegacyDuplicateRouteDoesTheSame(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);
        var newTenantId = Guid.NewGuid();

        using var response = await httpClient.PostAsJsonAsync(
            $"v1/admin/tenants/{TenantId}/duplicate",
            new CloneTenantRequest(newTenantId, "Visits", NewTenantPayload("visits.example", "https://visits.example")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(await WithTenantConfigAsync(factory, db => db.Tenants.AnyAsync(t => t.Id == newTenantId)));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Clone_IsRefusedForATenantAdminOrAnotherTenant(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);
        var request = new CloneTenantRequest(Guid.NewGuid(), "Visits", NewTenantPayload("visits.example", "https://visits.example"));

        var otherTenant = await ApiErrorAsync(() => client.CloneTenantAsync(OtherTenantId, request));
        CallAs(factory, httpClient, Admin());
        var asAdmin = await ApiErrorAsync(() => client.CloneTenantAsync(TenantId, request));

        Assert.Equal(403, otherTenant.Status);
        Assert.Equal(403, asAdmin.Status);
        Assert.Equal(1, await WithTenantConfigAsync(factory, db => db.Tenants.CountAsync()));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Clone_BadRequestsAreRefusedWithoutCreatingATenant(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);
        await client.CloneTenantAsync(
            TenantId, new CloneTenantRequest(Guid.NewGuid(), "Visits", NewTenantPayload("visits.example", "https://visits.example")));

        var sameId = await ApiErrorAsync(() => client.CloneTenantAsync(
            TenantId, new CloneTenantRequest(TenantId, "Copy", NewTenantPayload("copy.example", "https://copy.example"))));
        var hostnameTaken = await ApiErrorAsync(() => client.CloneTenantAsync(
            TenantId, new CloneTenantRequest(Guid.NewGuid(), "Copy", NewTenantPayload("visits.example", "https://copy.example"))));
        var missingSettings = await ApiErrorAsync(() => client.CloneTenantAsync(
            TenantId, new CloneTenantRequest(Guid.NewGuid(), "Copy", NewTenantPayload("copy.example", "https://copy.example", NewTenantSettings[..1]))));
        var notBase64 = await ApiErrorAsync(() => client.CloneTenantAsync(
            TenantId, new CloneTenantRequest(Guid.NewGuid(), "Copy", "not base64!")));

        Assert.Equal(400, sameId.Status);
        Assert.Equal(400, hostnameTaken.Status);
        Assert.Contains("visits.example", hostnameTaken.Message);
        Assert.Equal(400, missingSettings.Status);
        Assert.Equal(400, notBase64.Status);
        Assert.Equal(2, await WithTenantConfigAsync(factory, db => db.Tenants.CountAsync()));
    }

    // ---- health, cookbook and event catalogue ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Health_TenantAdminSeesNoPlatformDetails(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantWithSecretAsync(factory, client, httpClient);

        var asSuperAdmin = await client.GetTenantHealthAsync(TenantId);
        CallAs(factory, httpClient, Admin());
        var asAdmin = await client.GetTenantHealthAsync(TenantId);
        var other = await ApiErrorAsync(() => client.GetTenantHealthAsync(OtherTenantId));

        Assert.Equal(TenantId, asAdmin.TenantId);
        Assert.Contains(asAdmin.Checks, c => c.Code == "settings-present" && c.Status == "Pass");
        Assert.Empty(asAdmin.EffectiveConfiguration.FrontendOrigins);
        Assert.Equal(0, asAdmin.EffectiveConfiguration.ActiveTenantCount);
        Assert.Equal(0, asAdmin.EffectiveConfiguration.RegisteredAuthProviderCount);
        Assert.DoesNotContain(asAdmin.Checks, c => c.Detail.Contains("https://localhost:7020"));
        Assert.Contains("https://localhost:7020", asSuperAdmin.EffectiveConfiguration.FrontendOrigins);
        Assert.Equal(403, other.Status);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Cookbook_TenantAdminGetsNoSuperAdminOnlyCategories(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var asAdmin = await client.GetCategoryCookbookAsync();
        CallAs(factory, httpClient, SuperAdmin());
        var asSuperAdmin = await client.GetCategoryCookbookAsync();

        Assert.NotEmpty(asAdmin.Categories);
        Assert.DoesNotContain(asAdmin.Categories, e => SuperAdminOnlyTenantSettingCategories.IsRestricted(e.Category));
        Assert.Contains(asSuperAdmin.Categories, e => e.Category == SuperAdminOnlyTenantSettingCategories.ConnectionStrings);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CookbookAndEventCatalogue_AreRefusedForAServicePrincipal(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var catalogue = await client.GetEventCatalogueAsync();
        CallAs(factory, httpClient, [.. Admin(), new(TenantAuthClaimTypes.IsService, "true")]);
        var cookbook = await ApiErrorAsync(() => client.GetCategoryCookbookAsync());
        var events = await ApiErrorAsync(() => client.GetEventCatalogueAsync());

        Assert.NotEmpty(catalogue.Events);
        Assert.Equal(403, cookbook.Status);
        Assert.Equal(403, events.Status);
    }

    // ---- helpers ----

    /// <summary>The smallest DfE Sign-In tenant that passes the new-tenant checks.</summary>
    private static readonly (string Category, string Target, string Json)[] NewTenantSettings =
    [
        ("ConnectionStrings", "Api", """{"DefaultConnection":"Server=db;Database=ea;"}"""),
        ("Authorization", "Api", """{"TokenSettings":{"SecretKey":"kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk","Issuer":"iss","Audience":"aud"}}"""),
        ("AzureAd", "Api", """{"TenantId":"entra-tenant","ClientId":"api-client"}"""),
        ("Authentication", "Web", """{"Scheme":"DfESignIn"}"""),
        ("Layout", "Web", """{"ServiceName":"Visits"}"""),
        ("DfESignIn", "Shared", """{"Authority":"https://signin.example.test","DiscoveryEndpoint":"https://signin.example.test/.well-known/openid-configuration","ClientId":"dsi-client","ClientSecret":"dsi-value"}"""),
    ];

    private static string NewTenantPayload(
        string hostname,
        string origin,
        (string Category, string Target, string Json)[]? settings = null) =>
        ToBase64(JsonSerializer.Serialize(new
        {
            Hostname = hostname,
            FrontendOrigin = origin,
            Settings = (settings ?? NewTenantSettings).Select(s => new { s.Category, s.Target, SettingsJson = s.Json }),
        }));

    /// <summary>Seeds the tenant with one secret Layout row and leaves the caller signed in as a SuperAdmin.</summary>
    private static async Task SeedTenantWithSecretAsync(
        CustomWebApplicationDbContextFactory<Program> factory, ITenantAdminClient client, HttpClient httpClient)
    {
        await SeedTenantAsync(factory);
        CallAs(factory, httpClient, SuperAdmin());
        await client.UpsertTenantSettingAsync(
            TenantId, Setting("Layout", $$$"""{"Auth":{"ClientId":"app","ClientSecret":"{{{StoredSecret}}}"}}""", isSecret: true));
    }
}
