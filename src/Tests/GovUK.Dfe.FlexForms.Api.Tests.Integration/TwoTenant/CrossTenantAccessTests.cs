using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Api.Hubs;
using GovUK.Dfe.FlexForms.Api.Middleware;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant;

/// <summary>
/// A user signed in to tenant A, with a real tenant A token, reaching for tenant B's application,
/// files and template by id. Each case asserts the status the handler returns today (403 or 404),
/// a positive control shows the same caller can read its own tenant's equivalent, and the write cases
/// leave tenant B's data as seeded.
/// </summary>
public sealed class CrossTenantAccessTests(TwoTenantApi api) : IClassFixture<TwoTenantApi>
{
    private static readonly TestTenant A = TestTenant.A;
    private static readonly TestTenant B = TestTenant.B;

    /// <summary>Every request a caller can aim at <paramref name="target"/>'s resources.</summary>
    private static IEnumerable<CrossTenantCase> Cases(TestTenant target, TestTenant own) =>
    [
        new("read application", "GET", $"v1/applications/reference/{target.ApplicationReference}"),
        new("save a response", "POST", $"v1/applications/{target.ApplicationId}/responses", Json(new { ResponseBody = """{"name":"x"}""" })),
        new("submit", "POST", $"v1/applications/{target.ApplicationId}/submit"),
        new("list contributors", "GET", $"v1/applications/{target.ApplicationId}/contributors"),
        new("add a contributor", "POST", $"v1/applications/{target.ApplicationId}/contributors", Json(new { Name = "Eve", Email = "eve@example.com" })),
        new("remove a contributor", "DELETE", $"v1/applications/{target.ApplicationId}/contributors/{target.CaseworkerId}"),
        new("list files", "GET", $"v1/applications/{target.ApplicationId}/files"),
        new("download a file", "GET", $"v1/applications/{target.ApplicationId}/files/{target.FileId}/download"),
        new("upload a file", "POST", $"v1/applications/{target.ApplicationId}/files", Upload),
        new("delete a file", "DELETE", $"v1/applications/{target.ApplicationId}/files/{target.FileId}"),
        new("download their file through own application", "GET", $"v1/applications/{own.ApplicationId}/files/{target.FileId}/download"),
        new("download the preview", "GET", $"v1/applications/reference/{target.ApplicationReference}/preview/download"),
        new("read the validation gate", "GET", $"v1/applications/{target.ApplicationId}/file-validation-gate"),
        new("change the owner", "PUT", $"v1/applications/{target.ApplicationId}/owner", Json(new { Email = own.AdminEmail })),
        new("delete the application", "DELETE", $"v1/applications/{target.ApplicationId}"),
        new("list applications for the template", "GET", $"v1/applications/templates/{target.TemplateId}"),
        new("create an application on the template", "POST", "v1/applications", Json(new { target.TemplateId, InitialResponseBody = "{}" })),
        new("read the schema", "GET", $"v1/templates/{target.TemplateId}/schema"),
        new("list versions", "GET", $"v1/templates/{target.TemplateId}/versions"),
        new("read a version", "GET", $"v1/templates/{target.TemplateId}/versions/1.0.0"),
        new("list custom statuses", "GET", $"v1/templates/{target.TemplateId}/custom-statuses"),
        new("create a custom status", "POST", $"v1/templates/{target.TemplateId}/custom-statuses", Json(new { ApplicationStatus = "Submitted", Label = "Received" })),
        new("set the template live", "PUT", $"v1/templates/{target.TemplateId}/live", Json(new { IsLive = false })),
        new("grant the template to all users", "POST", $"v1/templates/{target.TemplateId}/grant-all-users"),
        new("create a version", "POST", $"v1/templates/{target.TemplateId}/versions",
            Json(new { VersionNumber = "9.9.9", JsonSchema = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"type":"object"}""")) })),
    ];

    /// <summary>What each request returns today for a tenant A admin and a tenant A owner.</summary>
    private static readonly Dictionary<string, (int Admin, int Owner)> Expected = new()
    {
        ["read application"] = (403, 403),
        ["save a response"] = (404, 403),
        ["submit"] = (404, 403),
        ["list contributors"] = (404, 403),
        ["add a contributor"] = (404, 403),
        ["remove a contributor"] = (404, 403),
        ["list files"] = (404, 403),
        ["download a file"] = (404, 403),
        ["upload a file"] = (404, 403),
        ["delete a file"] = (404, 403),
        ["download their file through own application"] = (404, 404),
        ["download the preview"] = (404, 404),
        ["read the validation gate"] = (404, 403),
        ["change the owner"] = (404, 403),
        ["delete the application"] = (403, 403),
        ["list applications for the template"] = (403, 403),
        ["create an application on the template"] = (403, 403),
        ["read the schema"] = (403, 403),
        ["list versions"] = (403, 403),
        ["read a version"] = (403, 403),
        ["list custom statuses"] = (403, 403),
        ["create a custom status"] = (403, 403),
        ["set the template live"] = (403, 403),
        ["grant the template to all users"] = (403, 403),
        ["create a version"] = (403, 403),
    };

    public static TheoryData<string, string> CallersAndCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in new[] { "admin", "owner" })
            foreach (var name in Expected.Keys)
                data.Add(caller, name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CallersAndCases))]
    public async Task ATenantAUserCannotReachTenantBsResourcesById(string caller, string caseName)
    {
        var token = await api.SignInAsync(A, caller == "admin" ? A.AdminEmail : A.OwnerEmail);
        var request = Cases(B, A).Single(c => c.Name == caseName);
        var expected = caller == "admin" ? Expected[caseName].Admin : Expected[caseName].Owner;

        using var response = await SendAsync(api.Client(A, token), request);

        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    public static TheoryData<string, string> OwnTenantControls() => new()
    {
        { "admin", "read application" },
        { "admin", "list contributors" },
        { "admin", "list files" },
        { "admin", "download the preview" },
        { "admin", "read the validation gate" },
        { "admin", "list applications for the template" },
        { "admin", "read the schema" },
        { "admin", "list versions" },
        { "admin", "read a version" },
        { "admin", "list custom statuses" },
        { "owner", "read application" },
        { "owner", "list contributors" },
        { "owner", "list files" },
        { "owner", "download the preview" },
        { "owner", "read the validation gate" },
    };

    [Theory]
    [MemberData(nameof(OwnTenantControls))]
    public async Task TheSameCallerCanReadItsOwnTenantsEquivalent(string caller, string caseName)
    {
        var token = await api.SignInAsync(A, caller == "admin" ? A.AdminEmail : A.OwnerEmail);
        var request = Cases(A, B).Single(c => c.Name == caseName);

        using var response = await SendAsync(api.Client(A, token), request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RefusedWritesLeaveTenantBsDataAsSeeded()
    {
        var templateGrantsBefore = await TemplateGrantCountAsync(B);
        foreach (var email in new[] { A.AdminEmail, A.OwnerEmail })
        {
            var token = await api.SignInAsync(A, email);
            foreach (var request in Cases(B, A).Where(c => c.Method != "GET"))
            {
                using var response = await SendAsync(api.Client(A, token), request);
                Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{email}: {request.Name}");
            }
        }

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
        var applicationId = new ApplicationId(B.ApplicationId);
        var templateId = new TemplateId(B.TemplateId);
        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
        Assert.Equal(ApplicationStatus.InProgress, application.Status);
        Assert.Null(application.DeletedOn);
        Assert.Equal(new UserId(B.OwnerId), application.CreatedBy);
        Assert.Equal(1, await db.ApplicationResponses.CountAsync(r => r.ApplicationId == applicationId));
        Assert.Equal(1, await db.Files.CountAsync(f => f.ApplicationId == applicationId));
        Assert.False(await db.Users.AnyAsync(u => u.Email == "eve@example.com"));
        Assert.Equal(1, await db.Applications.CountAsync(a => a.TemplateVersion!.TemplateId == templateId));
        Assert.True((await db.Templates.AsNoTracking().SingleAsync(t => t.Id == templateId)).IsLive);
        Assert.Equal(1, await db.TemplateVersions.CountAsync(v => v.TemplateId == templateId));
        Assert.Equal(0, await db.CustomApplicationStatuses.CountAsync(s => s.TemplateId == templateId));
        Assert.Equal(templateGrantsBefore, await TemplateGrantCountAsync(B));
    }

    private async Task<int> TemplateGrantCountAsync(TestTenant tenant)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
        return await db.Permissions.CountAsync(p => p.ResourceType == ResourceType.Template
                                                    && p.ResourceKey == tenant.TemplateId.ToString());
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("user manager")]
    public async Task RemovingATenantBOnlyUserFromTenantALeavesTheirTenantBAccess(string caller)
    {
        var token = await api.SignInAsync(A, caller == "admin" ? A.AdminEmail : A.UserManagerEmail);

        using var _ = await api.Client(A, token).DeleteAsync($"v1/users/{B.CaseworkerId}/tenant");

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
        var membership = await db.TenantMemberships.AsNoTracking()
            .SingleAsync(m => m.TenantId == B.Id && m.UserId == new UserId(B.CaseworkerId));
        Assert.True(membership.IsActive);
        var caseworkerToken = await api.SignInAsync(B, B.CaseworkerEmail);
        using var stillWorks = await api.Client(B, caseworkerToken).GetAsync($"v1/applications/reference/{B.ApplicationReference}");
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    // ---- notifications hub ----

    [Fact]
    public async Task AHubTicketFromTenantACannotBeRedeemedInTenantB()
    {
        var token = await api.SignInAsync(A, TwoTenantSeeder.BothTenantsEmail);
        using var ticket = await api.Client(A, token).PostAsync("auth/hub-ticket", content: null);
        ticket.EnsureSuccessStatusCode();
        var redeemUrl = await RedeemUrlAsync(ticket);

        using var inB = await api.Client(B).GetAsync(redeemUrl);
        using var inA = await api.Client(A).GetAsync(redeemUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, inB.StatusCode);
        // The ticket is single use and the failed redeem in B did not consume it.
        Assert.Equal(HttpStatusCode.NoContent, inA.StatusCode);
    }

    [Fact]
    public async Task TheHubGroupComesFromTheCookiesTenant_NotTheTenantHeader()
    {
        var email = TwoTenantSeeder.BothTenantsEmail;
        var cookie = await HubCookieAsync(A, email);
        var hub = api.Services.GetRequiredService<IHubContext<NotificationHub>>();

        // Connect with tenant A's cookie while the connection claims to be for tenant B.
        await using var connection = Connect(cookie, claimedTenant: B);
        var received = new List<string>();
        connection.On<string>("test.message", message => { lock (received) received.Add(message); });
        await connection.StartAsync();

        await hub.Clients.Group($"tenant:{B.Id}:user:{email}").SendAsync("test.message", "for tenant B");
        await hub.Clients.Group($"tenant:{A.Id}:user:{email}").SendAsync("test.message", "for tenant A");

        await WaitForAsync(() => received.Contains("for tenant A"));
        lock (received) Assert.Equal(["for tenant A"], received);
    }

    // ---- helpers ----

    private sealed record CrossTenantCase(string Name, string Method, string Path, Func<HttpContent>? Body = null);

    private static Func<HttpContent> Json(object body) => () => JsonContent.Create(body);

    private static HttpContent Upload() =>
        new MultipartFormDataContent { { new StringContent("x"), "file", "x.pdf" }, { new StringContent("x"), "name" } };

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, CrossTenantCase request) =>
        client.SendAsync(new HttpRequestMessage(new HttpMethod(request.Method), request.Path) { Content = request.Body?.Invoke() });

    private static async Task<string> RedeemUrlAsync(HttpResponseMessage ticket)
    {
        using var json = JsonDocument.Parse(await ticket.Content.ReadAsStringAsync());
        return new Uri(json.RootElement.GetProperty("url").GetString()!).PathAndQuery;
    }

    private async Task<string> HubCookieAsync(TestTenant tenant, string email)
    {
        var token = await api.SignInAsync(tenant, email);
        using var ticket = await api.Client(tenant, token).PostAsync("auth/hub-ticket", content: null);
        ticket.EnsureSuccessStatusCode();
        using var redeemed = await api.Client(tenant).GetAsync(await RedeemUrlAsync(ticket));
        Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
        return redeemed.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private HubConnection Connect(string cookie, TestTenant claimedTenant) =>
        new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                options.Headers.Add(TenantResolutionMiddleware.TenantIdHeader, claimedTenant.Id.ToString());
                options.Headers.Add("Cookie", cookie);
            })
            .Build();

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        // Leave time for a message that should not arrive to arrive anyway.
        await Task.Delay(300);
    }
}
