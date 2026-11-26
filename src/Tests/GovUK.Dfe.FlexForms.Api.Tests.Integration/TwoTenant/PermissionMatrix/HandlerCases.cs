using System.Net.Http.Json;
using System.Text;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant.PermissionMatrix;

/// <summary>
/// One handler-gated request (Layer B) and the status each principal its policy lets through gets back.
/// <see cref="Build"/> may set up a target first (for example, a role to delete) using the Admin's client.
/// </summary>
public sealed record HandlerCase(
    string Name,
    string Key,
    Func<MatrixApi, Principal, Task<HttpRequestMessage>> Build,
    IReadOnlyDictionary<Principal, int> Expected)
{
    public override string ToString() => Name;
}

/// <summary>
/// Layer B: what the handlers behind TenantAdmin, Users, Roles and Templates return to each principal that
/// gets past the policy. Principals run in <see cref="Order"/>, so non-idempotent requests have fixed results.
/// Targets are chosen so cases don't change what other cases see.
/// </summary>
public static class HandlerCases
{
    /// <summary>Lower privilege first, so a refused caller never finds the target already changed.</summary>
    public static readonly Principal[] Order =
    [
        Principal.User, Principal.Contributor, Principal.Owner, Principal.Caseworker, Principal.FormManager,
        Principal.TemplateManager, Principal.UserManager, Principal.Service, Principal.FileValidation, Principal.ServiceAdmin,
        Principal.Admin, Principal.SuperAdmin,
    ];

    private static TestTenant A => TestTenant.A;

    private static string Base64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static readonly string Schema = Base64("""{"type":"object","properties":{"name":{"type":"string"}}}""");

    private static Func<MatrixApi, Principal, Task<HttpRequestMessage>> Send(
        string method, string path, Func<Principal, object?>? body = null) =>
        (_, principal) => Task.FromResult(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = body?.Invoke(principal) is { } content ? JsonContent.Create(content) : null,
        });

    private static Dictionary<Principal, int> Codes(params (Principal Principal, int Status)[] codes) =>
        codes.ToDictionary(c => c.Principal, c => c.Status);

    public static IReadOnlyList<HandlerCase> All { get; } =
    [
        // ---- Templates ----
        new("list accessible templates", "TemplatesController.GetAccessibleTemplatesAsync", Send("GET", "v1/templates"), Codes((Principal.User, 200), (Principal.Contributor, 200), (Principal.Owner, 200), (Principal.Caseworker, 200), (Principal.FormManager, 200), (Principal.TemplateManager, 200), (Principal.UserManager, 200), (Principal.Service, 401), (Principal.FileValidation, 401), (Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read the latest schema", "TemplatesController.GetLatestTemplateSchemaAsync", Send("GET", $"v1/templates/{A.TemplateId}/schema"), Codes((Principal.User, 200), (Principal.Contributor, 200), (Principal.Owner, 200), (Principal.Caseworker, 403), (Principal.FormManager, 403), (Principal.TemplateManager, 200), (Principal.ServiceAdmin, 401), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("list versions", "TemplatesController.GetTemplateVersionsAsync", Send("GET", $"v1/templates/{A.TemplateId}/versions"), Codes((Principal.User, 200), (Principal.Contributor, 200), (Principal.Owner, 200), (Principal.Caseworker, 403), (Principal.FormManager, 403), (Principal.TemplateManager, 200), (Principal.ServiceAdmin, 401), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read a version", "TemplatesController.GetTemplateSchemaByVersionAsync", Send("GET", $"v1/templates/{A.TemplateId}/versions/1.0.0"), Codes((Principal.User, 200), (Principal.Contributor, 200), (Principal.Owner, 200), (Principal.Caseworker, 403), (Principal.FormManager, 403), (Principal.TemplateManager, 200), (Principal.ServiceAdmin, 401), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("list custom statuses", "TemplatesController.GetCustomApplicationStatusesAsync", Send("GET", $"v1/templates/{A.TemplateId}/custom-statuses"), Codes((Principal.User, 200), (Principal.Contributor, 200), (Principal.Owner, 200), (Principal.Caseworker, 200), (Principal.FormManager, 200), (Principal.TemplateManager, 200), (Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("create a custom status", "TemplatesController.CreateCustomApplicationStatusAsync",
            Send("POST", $"v1/templates/{A.TemplateId}/custom-statuses", p => new { ApplicationStatus = "Submitted", Label = $"Received ({p})" }), Codes((Principal.FormManager, 403), (Principal.TemplateManager, 201), (Principal.ServiceAdmin, 401), (Principal.Admin, 201), (Principal.SuperAdmin, 201))),
        new("set the form live", "TemplatesController.SetTemplateLiveAsync", Send("PUT", $"v1/templates/{A.TemplateId}/live", _ => new { IsLive = true }), Codes((Principal.FormManager, 403), (Principal.TemplateManager, 200), (Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("grant a form to all users", "TemplatesController.GrantTemplateAccessToAllUsersAsync", async (api, principal) =>
        {
            using var admin = api.For(Principal.Admin);
            using var created = await admin.PostAsJsonAsync("v1/templates",
                new { Name = $"Granted form ({principal})", InitialVersionNumber = "1.0.0", JsonSchema = Schema });
            var form = await created.Content.ReadFromJsonAsync<TemplateIdOnly>();
            return new HttpRequestMessage(HttpMethod.Post, $"v1/templates/{form!.TemplateId}/grant-all-users");
        }, Codes((Principal.FormManager, 403), (Principal.TemplateManager, 403), (Principal.ServiceAdmin, 400), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("create a form", "TemplatesController.CreateTemplateAsync",
            Send("POST", "v1/templates", p => new { Name = $"Matrix form ({p})", InitialVersionNumber = "1.0.0", JsonSchema = Schema }), Codes((Principal.FormManager, 201), (Principal.TemplateManager, 201), (Principal.ServiceAdmin, 401), (Principal.Admin, 201), (Principal.SuperAdmin, 201))),
        new("create a version of a form the caller was not given", "TemplatesController.CreateTemplateVersionAsync", async (api, principal) =>
        {
            using var admin = api.For(Principal.Admin);
            using var created = await admin.PostAsJsonAsync("v1/templates",
                new { Name = $"Form not given ({principal})", InitialVersionNumber = "1.0.0", JsonSchema = Schema });
            var form = await created.Content.ReadFromJsonAsync<TemplateIdOnly>();
            return new HttpRequestMessage(HttpMethod.Post, $"v1/templates/{form!.TemplateId}/versions")
            {
                Content = JsonContent.Create(new { VersionNumber = "1.1.0", JsonSchema = Schema }),
            };
        }, Codes((Principal.FormManager, 403), (Principal.TemplateManager, 403), (Principal.ServiceAdmin, 401), (Principal.Admin, 201), (Principal.SuperAdmin, 201))),
        new("create a version (Q10: needs Template:{id}:Write)", "TemplatesController.CreateTemplateVersionAsync",
            Send("POST", $"v1/templates/{A.TemplateId}/versions", p => new { VersionNumber = $"2.{(int)p}.0", JsonSchema = Schema }), Codes((Principal.FormManager, 403), (Principal.TemplateManager, 201), (Principal.ServiceAdmin, 401), (Principal.Admin, 201), (Principal.SuperAdmin, 201))),

        // ---- Users ----
        new("list tenant users", "UsersController.GetTenantUsersAsync", Send("GET", "v1/users/tenant"), Codes((Principal.UserManager, 200), (Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read a user's permissions", "UsersController.GetUserPermissionsAsync", Send("GET", $"v1/users/{A.CaseworkerId}/permissions"), Codes((Principal.UserManager, 200), (Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("set a user's permissions", "UsersController.SetUserPermissionsAsync",
            Send("PUT", $"v1/users/{A.SpareMemberId(1)}/permissions", _ => new
            {
                Permissions = new[] { new { ResourceType = "Template", ResourceKey = A.TemplateId.ToString(), AccessType = "Read" } },
            }), Codes((Principal.UserManager, 200), (Principal.ServiceAdmin, 400), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("set a user's forms", "UsersController.UpdateUserTemplateAccessAsync",
            Send("PUT", $"v1/users/{A.SpareMemberId(1)}/templates", _ => new { TemplateIds = new[] { A.TemplateId } }), Codes((Principal.UserManager, 200), (Principal.ServiceAdmin, 400), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("assign a custom role", "UsersController.AssignUserRoleAsync",
            Send("POST", "v1/users/roles", _ => new { Email = A.SpareMemberEmail(3), Name = "Alpha Spare 3", Role = RoleTemplates.CaseworkerKey, TemplateIds = Array.Empty<Guid>() }), Codes((Principal.UserManager, 200), (Principal.ServiceAdmin, 400), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("make a user an Admin", "UsersController.AssignUserRoleAsync",
            Send("POST", "v1/users/roles", _ => new { Email = A.SpareMemberEmail(2), Name = "Alpha Spare 2", Role = RoleNames.Admin, TemplateIds = Array.Empty<Guid>() }), Codes((Principal.UserManager, 200), (Principal.ServiceAdmin, 400), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("remove an Admin from the tenant", "UsersController.RemoveUserFromTenantAsync",
            Send("DELETE", $"v1/users/{TwoTenantSeeder.BothTenantsUserId}/tenant"), Codes((Principal.UserManager, 204), (Principal.ServiceAdmin, 204), (Principal.Admin, 204), (Principal.SuperAdmin, 204))),
        new("read the access audit", "UsersController.GetAccessAuditLogAsync", Send("GET", "v1/users/access-audit"), Codes((Principal.UserManager, 200), (Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read a sign-in lock (test authentication off)", "UsersController.GetTestAuthSignInLockAsync", Send("GET", $"v1/users/{A.CaseworkerId}/test-auth-sign-in-lock"), Codes((Principal.UserManager, 403), (Principal.ServiceAdmin, 403), (Principal.Admin, 403), (Principal.SuperAdmin, 403))),
        new("set a sign-in lock (test authentication off)", "UsersController.SetTestAuthSignInLockAsync",
            Send("PUT", $"v1/users/{A.CaseworkerId}/test-auth-sign-in-lock", _ => new { IsLocked = false }), Codes((Principal.UserManager, 403), (Principal.ServiceAdmin, 403), (Principal.Admin, 403), (Principal.SuperAdmin, 403))),
        new("clear a temporary lockout (test authentication off)", "UsersController.ClearTestAuthTemporaryLockoutAsync",
            Send("DELETE", $"v1/users/{A.CaseworkerId}/test-auth-sign-in-lock/temporary"), Codes((Principal.UserManager, 403), (Principal.ServiceAdmin, 403), (Principal.Admin, 403), (Principal.SuperAdmin, 403))),
        new("invite a user to set up an authenticator (test authentication off)", "UsersController.CreateTestAuthAuthenticatorInviteAsync",
            Send("POST", "v1/users/test-auth-authenticator-invites", _ => new { Email = A.CaseworkerEmail }), Codes((Principal.UserManager, 403), (Principal.ServiceAdmin, 403), (Principal.Admin, 403), (Principal.SuperAdmin, 403))),
        new("list applications created by an email", "UsersController.GetCreatedApplicationsByEmailAsync",
            Send("GET", $"v1/users/created-applications?email={Uri.EscapeDataString(A.OwnerEmail)}"), Codes((Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),

        // ---- Roles ----
        new("list roles", "RolesController.ListAsync", Send("GET", "v1/roles"), Codes((Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read a role's permissions", "RolesController.GetPermissionsAsync", Send("GET", $"v1/roles/{A.CaseworkerRoleId}/permissions"), Codes((Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("create a role", "RolesController.CreateAsync", Send("POST", "v1/roles", p => new { Name = $"Matrix {p}" }), Codes((Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("create a role from a template that already exists", "RolesController.CreateFromTemplateAsync",
            Send("POST", "v1/roles/from-template", _ => new { TemplateKey = RoleTemplates.CaseworkerKey }), Codes((Principal.ServiceAdmin, 400), (Principal.Admin, 400), (Principal.SuperAdmin, 400))),
        new("rename a role to its own name", "RolesController.RenameAsync",
            Send("PUT", $"v1/roles/{A.TemplateManagerRoleId}", _ => new { Name = RoleTemplates.TemplateManagerKey }), Codes((Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("set a role's permissions", "RolesController.SetPermissionsAsync",
            Send("PUT", $"v1/roles/{A.TemplateManagerRoleId}/permissions", _ => new
            {
                Permissions = new[] { new { ResourceType = "Template", ResourceKey = "Any", AccessType = "Manage" } },
            }), Codes((Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("delete a role", "RolesController.DeleteAsync", async (api, principal) =>
        {
            using var admin = api.For(Principal.Admin);
            using var created = await admin.PostAsJsonAsync("v1/roles", new { Name = $"Doomed {principal}" });
            var role = await created.Content.ReadFromJsonAsync<RoleIdOnly>();
            return new HttpRequestMessage(HttpMethod.Delete, $"v1/roles/{role!.RoleId}");
        }, Codes((Principal.ServiceAdmin, 200), (Principal.Admin, 200), (Principal.SuperAdmin, 200))),

        // ---- TenantAdmin ----
        new("list tenants", "TenantAdminController.GetTenants", Send("GET", "v1/admin/tenants"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("refresh tenant configuration", "TenantAdminController.RefreshTenantConfiguration", Send("POST", "v1/admin/tenants/refresh"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("list platform tenants", "TenantAdminController.GetPlatformTenants", Send("GET", "v1/admin/tenants/platform"), Codes((Principal.Admin, 403), (Principal.SuperAdmin, 200))),
        new("read the event catalogue", "TenantAdminController.GetEventCatalogue", Send("GET", "v1/admin/tenants/event-catalogue"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read the settings cookbook", "TenantAdminController.GetCategoryCookbook", Send("GET", "v1/admin/tenants/settings/cookbook"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read the full settings", "TenantAdminController.GetTenantSettings", Send("GET", $"v1/admin/tenants/{A.Id}/settings"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("update a setting", "TenantAdminController.UpsertTenantSetting", async (api, _) =>
        {
            // Create the row first, so every caller is updating it whatever ran before.
            using var superAdmin = api.For(Principal.SuperAdmin);
            using var __ = await superAdmin.PostAsJsonAsync($"v1/admin/tenants/{A.Id}/settings",
                new { Category = "Dashboard", Target = "Web", SettingsJson = Base64("""{"PageSize":10}"""), IsSecret = false });
            return new HttpRequestMessage(HttpMethod.Post, $"v1/admin/tenants/{A.Id}/settings")
            {
                Content = JsonContent.Create(new { Category = "Dashboard", Target = "Web", SettingsJson = Base64("""{"PageSize":20}"""), IsSecret = false }),
            };
        }, Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("validate a setting", "TenantAdminController.ValidateTenantSetting",
            Send("POST", $"v1/admin/tenants/{A.Id}/settings/validate", _ => new { Category = "Dashboard", Target = "Web", SettingsJson = Base64("""{"PageSize":20}"""), IsSecret = false }), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("delete a setting", "TenantAdminController.DeleteTenantSetting", async (api, principal) =>
        {
            using var superAdmin = api.For(Principal.SuperAdmin);
            var category = principal == Principal.Admin ? "Dashboard" : "Banner";
            using var _ = await superAdmin.PostAsJsonAsync($"v1/admin/tenants/{A.Id}/settings",
                new { Category = category, Target = "Web", SettingsJson = Base64("""{"Text":"x"}"""), IsSecret = false });
            return new HttpRequestMessage(HttpMethod.Delete, $"v1/admin/tenants/{A.Id}/settings?category={category}&target=Web");
        }, Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("reveal a secret", "TenantAdminController.RevealTenantSettingSecret",
            Send("POST", $"v1/admin/tenants/{A.Id}/settings/reveal", _ => new { Category = "Layout", Target = "Web", Path = "Auth:ClientSecret", Reason = "Rotating the key in Entra" }), Codes((Principal.Admin, 403), (Principal.SuperAdmin, 200))),
        new("read the safe settings", "TenantAdminController.GetSafeTenantSettings", Send("GET", $"v1/admin/tenants/{A.Id}/safe-settings"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("upsert a safe setting", "TenantAdminController.UpsertSafeTenantSetting",
            Send("PUT", $"v1/admin/tenants/{A.Id}/safe-settings", _ => new { Category = "Dashboard", Target = "Web", SettingsJson = Base64("""{"PageSize":25}"""), IsSecret = false }), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read the settings audit", "TenantAdminController.GetSettingAuditLog", Send("GET", $"v1/admin/tenants/{A.Id}/settings/audit"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read tenant health", "TenantAdminController.GetTenantHealth", Send("GET", $"v1/admin/tenants/{A.Id}/health"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("read the effective configuration", "TenantAdminController.GetEffectiveConfiguration", Send("GET", $"v1/admin/tenants/{A.Id}/effective-config"), Codes((Principal.Admin, 200), (Principal.SuperAdmin, 200))),
        new("export the configuration", "TenantAdminController.ExportConfiguration", Send("GET", $"v1/admin/tenants/{A.Id}/export"), Codes((Principal.Admin, 403), (Principal.SuperAdmin, 200))),
        new("import a configuration", "TenantAdminController.ImportConfiguration",
            Send("POST", $"v1/admin/tenants/{A.Id}/import", _ => new
            {
                Settings = new[] { new { Category = "Dashboard", Target = "Web", SettingsJson = """{"PageSize":30}""", IsSecret = false } },
                SkipSecretPlaceholders = true,
            }), Codes((Principal.Admin, 403), (Principal.SuperAdmin, 200))),
        new("clone the tenant", "TenantAdminController.CloneTenant",
            Send("POST", $"v1/admin/tenants/{A.Id}/clone", p => new { NewTenantId = Guid.NewGuid(), NewTenantName = $"Clone {p}", PayloadJson = NewTenantPayload($"clone-{p}".ToLowerInvariant()) }), Codes((Principal.Admin, 403), (Principal.SuperAdmin, 201))),
        new("duplicate the tenant", "TenantAdminController.DuplicateTenant",
            Send("POST", $"v1/admin/tenants/{A.Id}/duplicate", p => new { NewTenantId = Guid.NewGuid(), NewTenantName = $"Copy {p}", PayloadJson = NewTenantPayload($"duplicate-{p}".ToLowerInvariant()) }), Codes((Principal.Admin, 403), (Principal.SuperAdmin, 201))),
    ];

    private sealed record RoleIdOnly(Guid RoleId);

    private sealed record TemplateIdOnly(Guid TemplateId);

    /// <summary>The smallest new-tenant payload that passes the new-tenant checks, for a unique hostname.</summary>
    private static string NewTenantPayload(string name) => Base64(System.Text.Json.JsonSerializer.Serialize(new
    {
        Hostname = $"{name}.example",
        FrontendOrigin = $"https://{name}.example",
        Settings = new[]
        {
            new { Category = "ConnectionStrings", Target = "Api", SettingsJson = """{"DefaultConnection":"Server=db;Database=ea;"}""" },
            new { Category = "Authorization", Target = "Api", SettingsJson = """{"TokenSettings":{"SecretKey":"kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk","Issuer":"iss","Audience":"aud"}}""" },
            new { Category = "AzureAd", Target = "Api", SettingsJson = """{"TenantId":"entra-tenant","ClientId":"api-client"}""" },
            new { Category = "Authentication", Target = "Web", SettingsJson = """{"Scheme":"DfESignIn"}""" },
            new { Category = "Layout", Target = "Web", SettingsJson = """{"ServiceName":"Copy"}""" },
            new { Category = "DfESignIn", Target = "Shared", SettingsJson = """{"Authority":"https://signin.example.test","DiscoveryEndpoint":"https://signin.example.test/.well-known/openid-configuration","ClientId":"dsi-client","ClientSecret":"dsi-value"}""" },
        },
    }));
}
