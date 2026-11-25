using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant;

/// <summary>
/// Who can see, edit and submit an application, how contributors and roles are managed, and removing a user
/// from the tenant, over HTTP with real tenant A tokens. Each test creates the application it works on, so the
/// tests don't share state.
/// </summary>
public sealed class ApplicationAccessWorkflowTests(TwoTenantApi api) : IClassFixture<TwoTenantApi>
{
    private static readonly TestTenant A = TestTenant.A;

    [Fact]
    public async Task AnAdminCanSaveAResponseOnAnotherUsersApplication()
    {
        var application = await CreateApplicationAsOwnerAsync();
        using var admin = await SignedInAsync(A.AdminEmail);

        using var saved = await admin.PostAsJsonAsync(
            $"v1/applications/{application.Id}/responses",
            new { ResponseBody = Base64("""{"name":"Saved by the admin"}""") });

        Assert.True(saved.IsSuccessStatusCode, $"{(int)saved.StatusCode}");
        using var read = await admin.GetAsync($"v1/applications/reference/{application.Reference}");
        Assert.Contains("Saved by the admin", await read.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMemberCannotSeeOrOpenAnApplicationThatIsNotSharedWithThem()
    {
        var application = await CreateApplicationAsOwnerAsync();
        using var member = await SignedInAsync(A.MemberEmail);

        using var dashboard = await member.GetAsync("v1/me/applications");
        using var opened = await member.GetAsync($"v1/applications/reference/{application.Reference}");

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.DoesNotContain(application.Reference, await dashboard.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, opened.StatusCode);
    }

    [Fact]
    public async Task OnlyTheLeadApplicantCanSubmit()
    {
        var application = await CreateApplicationAsOwnerAsync();
        var before = await StatusAsync(application.Id);
        using var admin = await SignedInAsync(A.AdminEmail);

        using var submitted = await admin.PostAsync($"v1/applications/{application.Id}/submit", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, submitted.StatusCode);
        Assert.Equal(before, await StatusAsync(application.Id));
        Assert.NotEqual(ApplicationStatus.Submitted, before);

        // The lead applicant is not refused for being who they are (the form may still be incomplete).
        using var owner = await SignedInAsync(A.OwnerEmail);
        using var byOwner = await owner.PostAsync($"v1/applications/{application.Id}/submit", content: null);
        Assert.NotEqual(HttpStatusCode.Forbidden, byOwner.StatusCode);
    }

    [Fact]
    public async Task AnAddedContributorCanEditUntilTheyAreRemoved()
    {
        var application = await CreateApplicationAsOwnerAsync();
        using var owner = await SignedInAsync(A.OwnerEmail);
        var contributorEmail = $"contributor.{application.Reference.ToLowerInvariant()}@alpha.example";

        using var added = await owner.PostAsJsonAsync(
            $"v1/applications/{application.Id}/contributors", new { Name = "Workflow Contributor", Email = contributorEmail });
        Assert.True(added.IsSuccessStatusCode, $"{(int)added.StatusCode}");
        var contributorId = await JsonGuidAsync(added, "userId");

        using (var contributor = await SignedInAsync(contributorEmail))
        {
            using var dashboard = await contributor.GetAsync("v1/me/applications");
            using var saved = await contributor.PostAsJsonAsync(
                $"v1/applications/{application.Id}/responses",
                new { ResponseBody = Base64("""{"name":"Saved by the contributor"}""") });

            Assert.Contains(application.Reference, await dashboard.Content.ReadAsStringAsync());
            Assert.True(saved.IsSuccessStatusCode, $"{(int)saved.StatusCode}");
        }

        using var removed = await owner.DeleteAsync($"v1/applications/{application.Id}/contributors/{contributorId}");
        Assert.True(removed.IsSuccessStatusCode, $"{(int)removed.StatusCode}");

        // Only application access is checked here; what removal does to file grants is tracked separately.
        using var afterwards = await SignedInAsync(contributorEmail);
        using var opened = await afterwards.GetAsync($"v1/applications/reference/{application.Reference}");
        using var saveAgain = await afterwards.PostAsJsonAsync(
            $"v1/applications/{application.Id}/responses", new { ResponseBody = Base64("""{"name":"After removal"}""") });
        Assert.Equal(HttpStatusCode.Forbidden, opened.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, saveAgain.StatusCode);
    }

    [Fact]
    public async Task AnAdminCanSeeWhoHasAccessByReferenceAndByTheLeadApplicantsEmail()
    {
        var application = await CreateApplicationAsOwnerAsync();
        var contributorEmail = $"invitee.{application.Reference.ToLowerInvariant()}@alpha.example";
        using (var owner = await SignedInAsync(A.OwnerEmail))
        {
            using var added = await owner.PostAsJsonAsync(
                $"v1/applications/{application.Id}/contributors", new { Name = "Workflow Invitee", Email = contributorEmail });
            Assert.True(added.IsSuccessStatusCode, $"{(int)added.StatusCode}");
        }

        using var admin = await SignedInAsync(A.AdminEmail);
        using var byReference = await admin.GetAsync($"v1/applications/{application.Id}/contributors");
        using var byEmail = await admin.GetAsync($"v1/users/created-applications?email={Uri.EscapeDataString(A.OwnerEmail)}");

        Assert.Equal(HttpStatusCode.OK, byReference.StatusCode);
        Assert.Contains(contributorEmail, await byReference.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
        var created = await byEmail.Content.ReadAsStringAsync();
        Assert.Contains(application.Reference, created);
        Assert.Contains(contributorEmail, created);
    }

    [Fact]
    public async Task ACustomRoleCanBeDeletedOnceNoOneHoldsIt()
    {
        using var admin = await SignedInAsync(A.AdminEmail);
        using var created = await admin.PostAsJsonAsync("v1/roles", new { Name = "Workflow reviewer" });
        Assert.True(created.IsSuccessStatusCode, $"{(int)created.StatusCode}");
        var roleId = await JsonGuidAsync(created, "roleId");

        using var assigned = await admin.PostAsJsonAsync("v1/users/roles",
            new { Email = A.SpareMemberEmail(1), Name = "Alpha Spare 1", Role = "Workflow reviewer", TemplateIds = Array.Empty<Guid>() });
        Assert.True(assigned.IsSuccessStatusCode, $"{(int)assigned.StatusCode}");
        Assert.Equal(roleId, await MembershipRoleAsync(A.SpareMemberId(1)));

        using var whileHeld = await admin.DeleteAsync($"v1/roles/{roleId}");
        using var moved = await admin.PostAsJsonAsync("v1/users/roles",
            new { Email = A.SpareMemberEmail(1), Name = "Alpha Spare 1", Role = RoleTemplates.CaseworkerKey, TemplateIds = Array.Empty<Guid>() });
        using var deleted = await admin.DeleteAsync($"v1/roles/{roleId}");
        using var list = await admin.GetAsync("v1/roles");

        Assert.Equal(HttpStatusCode.BadRequest, whileHeld.StatusCode);
        Assert.Contains("cannot be deleted while users are assigned", await whileHeld.Content.ReadAsStringAsync());
        Assert.True(moved.IsSuccessStatusCode, $"{(int)moved.StatusCode}");
        Assert.True(deleted.IsSuccessStatusCode, $"{(int)deleted.StatusCode}");
        Assert.DoesNotContain("Workflow reviewer", await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AUserRemovedFromTheTenantCanNoLongerSignIn()
    {
        Assert.NotNull(await api.SignInAsync(A, A.SpareMemberEmail(2)));
        using var admin = await SignedInAsync(A.AdminEmail);

        using var removed = await admin.DeleteAsync($"v1/users/{A.SpareMemberId(2)}/tenant");
        using var exchange = await api.ExchangeAsync(A, A.SpareMemberEmail(2));

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, exchange.StatusCode);
        Assert.False(await IsActiveMemberAsync(A.SpareMemberId(2)));
    }

    // ---- helpers ----

    private sealed record CreatedApplication(Guid Id, string Reference);

    private async Task<HttpClient> SignedInAsync(string email) => api.Client(A, await api.SignInAsync(A, email));

    private async Task<CreatedApplication> CreateApplicationAsOwnerAsync()
    {
        using var owner = await SignedInAsync(A.OwnerEmail);
        using var response = await owner.PostAsJsonAsync("v1/applications",
            new { A.TemplateId, InitialResponseBody = Base64("""{"name":"Workflow applicant"}""") });
        Assert.True(response.IsSuccessStatusCode, $"create application: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new(json.RootElement.GetProperty("applicationId").GetGuid(), json.RootElement.GetProperty("applicationReference").GetString()!);
    }

    private static async Task<Guid> JsonGuidAsync(HttpResponseMessage response, string property)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty(property).GetGuid();
    }

    private static string Base64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private async Task<ApplicationStatus?> StatusAsync(Guid applicationId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
        var id = new ApplicationId(applicationId);
        return (await db.Applications.AsNoTracking().SingleAsync(a => a.Id == id)).Status;
    }

    private async Task<Guid?> MembershipRoleAsync(Guid userId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
        var id = new UserId(userId);
        var membership = await db.TenantMemberships.AsNoTracking().SingleAsync(m => m.TenantId == A.Id && m.UserId == id);
        return membership.RoleId.Value;
    }

    private async Task<bool> IsActiveMemberAsync(Guid userId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
        var id = new UserId(userId);
        return await db.TenantMemberships.AsNoTracking().AnyAsync(m => m.TenantId == A.Id && m.UserId == id && m.IsActive);
    }
}
