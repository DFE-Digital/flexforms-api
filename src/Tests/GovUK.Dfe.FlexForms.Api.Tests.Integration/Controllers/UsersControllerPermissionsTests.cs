using System.Net.Http.Headers;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

/// <summary>
/// Direct permission administration for one tenant: GET/PUT /users/{id}/permissions and
/// DELETE /users/{id}/tenant. Cross-tenant cases (a user who belongs to two tenants) are covered with chunk 1.9.
/// A "user manager" here has <c>User:Any:Manage</c> and no Admin role. Decision 7: their powers are intended
/// as they are, so these tests pin that a user manager can remove an admin and can grant tenant-wide read.
/// </summary>
public class UsersControllerPermissionsTests
{
    private const string AliceEmail = "alice@example.com";
    private static readonly Guid TenantId = Guid.Parse(EaContextSeeder.TestTenantId);
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid BobId = Guid.Parse(EaContextSeeder.BobId);
    private static readonly Guid AliceId = Guid.Parse(EaContextSeeder.AliceId);
    private static readonly string SeededTemplate = EaContextSeeder.TemplateId;
    private static readonly string SeededApplication = EaContextSeeder.ApplicationId;

    // ---- GET /users/{id}/permissions ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetUserPermissions_ReturnsOnlyTheCurrentTenantsDirectGrants(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        var other = await SeedOtherTenantApplicationAsync(factory);
        await AddGrantAsync(factory, BobId, ResourceType.Template, other.TemplateId.ToString(), AccessType.Read);
        await AddGrantAsync(factory, BobId, ResourceType.Application, other.ApplicationId.ToString(), AccessType.Read, other.ApplicationId);
        CallAs(factory, httpClient, Admin());

        var grants = await usersClient.GetUserPermissionsAsync(BobId);

        Assert.Contains(grants, g => g.ResourceType == ResourceType.Application && g.ResourceKey == SeededApplication && g.AccessType == AccessType.Write);
        Assert.DoesNotContain(grants, g => g.ResourceKey == other.TemplateId.ToString());
        Assert.DoesNotContain(grants, g => g.ResourceKey == other.ApplicationId.ToString());
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetUserPermissions_IsRefusedWithoutUserManagement(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Bob());

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.GetUserPermissionsAsync(AliceId));

        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetUserPermissions_IsNotFound_ForAnUnknownUserOrANonMember(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        var nonMember = await SeedUserAsync(factory, membershipRole: null);
        CallAs(factory, httpClient, Admin());

        var unknown = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.GetUserPermissionsAsync(Guid.NewGuid()));
        var notMember = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.GetUserPermissionsAsync(nonMember));

        Assert.Equal(404, unknown.StatusCode);
        Assert.Equal(404, notMember.StatusCode);
        Assert.Equal("User is not an active member of this tenant", notMember.Result?.Message);
    }

    // ---- PUT /users/{id}/permissions ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_ReplacesOnlyTheCurrentTenantsGrants(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        var other = await SeedOtherTenantApplicationAsync(factory);
        await AddGrantAsync(factory, BobId, ResourceType.Template, other.TemplateId.ToString(), AccessType.Read);
        CallAs(factory, httpClient, Admin());

        var result = await usersClient.SetUserPermissionsAsync(
            BobId,
            Request((ResourceType.Template, SeededTemplate, AccessType.Write)));

        var stored = await GrantsAsync(factory, BobId);
        // This tenant's grants are replaced: the seeded application grants are gone, the new one is there.
        Assert.DoesNotContain(stored, p => p.ResourceType == ResourceType.Application && p.ResourceKey == SeededApplication);
        Assert.Contains(stored, p => p.ResourceType == ResourceType.Template && p.ResourceKey == SeededTemplate && p.AccessType == AccessType.Write);
        // Another tenant's grant is left alone, and isn't returned.
        Assert.Contains(stored, p => p.ResourceKey == other.TemplateId.ToString());
        Assert.DoesNotContain(result, g => g.ResourceKey == other.TemplateId.ToString());
        var granted = Assert.Single(stored, p => p.ResourceKey == SeededTemplate);
        Assert.Equal(AliceId, granted.GrantedBy.Value);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_StoresNotificationKeysAgainstTheTenant(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());

        var result = await usersClient.SetUserPermissionsAsync(
            BobId,
            Request((ResourceType.Notifications, EaContextSeeder.BobEmail, AccessType.Read)));

        var stored = Assert.Single(await GrantsAsync(factory, BobId), p => p.ResourceType == ResourceType.Notifications);
        Assert.Equal($"{TenantId:D}:{EaContextSeeder.BobEmail}", stored.ResourceKey);
        // Callers see the plain identity.
        Assert.Contains(result, g => g.ResourceType == ResourceType.Notifications && g.ResourceKey == EaContextSeeder.BobEmail);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    // The cache clear itself is checked in SetUserPermissionsCommandHandlerTests; the test host doesn't cache /me/permissions.
    public async Task SetUserPermissions_ShowUpOnTheUsersNextRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        CallAs(factory, httpClient, Bob());
        var before = await usersClient.GetMyPermissionsAsync();
        Assert.DoesNotContain(before.Permissions!, p => p.ResourceType == ResourceType.Application && p.ResourceKey == PermissionConstants.AnyResourceKey && p.AccessType == AccessType.Read);

        CallAs(factory, httpClient, Admin());
        await usersClient.SetUserPermissionsAsync(BobId, Request((ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read)));

        CallAs(factory, httpClient, Bob());
        var after = await usersClient.GetMyPermissionsAsync();
        Assert.Contains(after.Permissions!, p => p.ResourceType == ResourceType.Application && p.ResourceKey == PermissionConstants.AnyResourceKey && p.AccessType == AccessType.Read);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_UserManagerCanGrantTenantWideRead(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, UserManager());

        var result = await usersClient.SetUserPermissionsAsync(
            BobId,
            Request((ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read)));

        Assert.Contains(result, g => g.ResourceType == ResourceType.Application && g.ResourceKey == PermissionConstants.AnyResourceKey && g.AccessType == AccessType.Read);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_IsRefusedWithoutUserManagement(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Bob());

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.SetUserPermissionsAsync(BobId, Request((ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read))));

        Assert.Equal(403, ex.StatusCode);
        Assert.DoesNotContain(await GrantsAsync(factory, BobId), p => p.ResourceKey == PermissionConstants.AnyResourceKey);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_IsNotFound_ForAnUnknownUserOrANonMember(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        var nonMember = await SeedUserAsync(factory, membershipRole: null);
        CallAs(factory, httpClient, Admin());
        var request = Request((ResourceType.Template, SeededTemplate, AccessType.Read));

        var unknown = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.SetUserPermissionsAsync(Guid.NewGuid(), request));
        var notMember = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.SetUserPermissionsAsync(nonMember, request));

        Assert.Equal(404, unknown.StatusCode);
        Assert.Equal(404, notMember.StatusCode);
        Assert.Empty(await GrantsAsync(factory, nonMember));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_RejectsAManageGrant(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.SetUserPermissionsAsync(BobId, Request((ResourceType.Template, PermissionConstants.AnyResourceKey, AccessType.Manage))));

        Assert.Equal(400, ex.StatusCode);
        Assert.DoesNotContain(await GrantsAsync(factory, BobId), p => p.AccessType == AccessType.Manage);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_RefusesAnApplicationKeyThatIsNotAGuidDoesNotExistOrIsInAnotherTenant(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        var other = await SeedOtherTenantApplicationAsync(factory);
        CallAs(factory, httpClient, Admin());
        var cases = new Dictionary<string, string>
        {
            ["not-a-guid"] = "Validation failed", // shape check in the validator, before the handler
            [Guid.NewGuid().ToString()] = "was not found",
            [other.ApplicationId.ToString()] = "does not belong to the current tenant"
        };

        foreach (var (key, message) in cases)
        {
            var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
                () => usersClient.SetUserPermissionsAsync(BobId, Request((ResourceType.Application, key, AccessType.Read))));

            Assert.Equal(400, ex.StatusCode);
            Assert.Contains(message, ex.Result?.Message ?? "");
        }

        // Nothing was replaced: Bob still has his seeded grants.
        Assert.Contains(await GrantsAsync(factory, BobId), p => p.ResourceKey == SeededApplication && p.AccessType == AccessType.Write);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_RefusesATemplateOutsideTheTenantsCatalogue(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        var other = await SeedOtherTenantApplicationAsync(factory);
        CallAs(factory, httpClient, Admin());

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.SetUserPermissionsAsync(BobId, Request((ResourceType.Template, other.TemplateId.ToString(), AccessType.Read))));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("was not found in the current tenant", ex.Result?.Message ?? "");
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetUserPermissions_RefusesANotificationOrUserGrantForAnUnknownEmail(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.SetUserPermissionsAsync(BobId, Request((ResourceType.Notifications, "nobody@example.com", AccessType.Read))));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("'nobody@example.com' was not found", ex.Result?.Message ?? "");
    }

    // ---- DELETE /users/{id}/tenant ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RemoveUserFromTenant_RemovesTemplateAccessKeepsApplicationGrantsAndDeactivatesTheMembership(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        await AddGrantAsync(factory, BobId, ResourceType.Template, SeededTemplate, AccessType.Write);
        await AddGrantAsync(factory, BobId, ResourceType.Template, PermissionConstants.AnyResourceKey, AccessType.Write);
        CallAs(factory, httpClient, Admin());

        await usersClient.RemoveUserFromTenantAsync(BobId);

        var stored = await GrantsAsync(factory, BobId);
        Assert.DoesNotContain(stored, p => p.ResourceType == ResourceType.Template);
        Assert.Contains(stored, p => p.ResourceType == ResourceType.Application && p.ResourceKey == SeededApplication && p.AccessType == AccessType.Write);
        Assert.Contains(stored, p => p.ResourceType == ResourceType.ApplicationFiles && p.ResourceKey == SeededApplication);
        Assert.False(await IsActiveMemberAsync(factory, BobId));

        var audit = Assert.Single(
            factory.GetDbContext<ExternalApplicationsContext>().TenantAccessAudits.AsNoTracking()
                .Where(a => a.SubjectUserId == BobId && a.Action == "MembershipDeactivated"));
        Assert.Equal(TenantId, audit.TenantId);
        Assert.Equal(AliceId, audit.ActorUserId);
        Assert.Equal(AliceEmail, audit.ActorEmail);
        Assert.Equal(EaContextSeeder.BobEmail, audit.SubjectEmail);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RemoveUserFromTenant_UserManagerCanRemoveAnAdmin(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        var admin = await SeedUserAsync(factory, membershipRole: RoleNames.Admin);
        CallAs(factory, httpClient, UserManager());

        await usersClient.RemoveUserFromTenantAsync(admin);

        Assert.False(await IsActiveMemberAsync(factory, admin));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RemoveUserFromTenant_RefusesToRemoveYourself(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.RemoveUserFromTenantAsync(AliceId));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("You cannot remove yourself from the tenant", ex.Result?.Message);
        Assert.True(await IsActiveMemberAsync(factory, AliceId));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RemoveUserFromTenant_IsRefusedWithoutUserManagement_AndNotFoundForAnUnknownUser(
        CustomWebApplicationDbContextFactory<Program> factory, IUsersClient usersClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Bob());
        var refused = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.RemoveUserFromTenantAsync(AliceId));

        CallAs(factory, httpClient, Admin());
        var unknown = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.RemoveUserFromTenantAsync(Guid.NewGuid()));

        Assert.Equal(403, refused.StatusCode);
        Assert.Equal(404, unknown.StatusCode);
        Assert.True(await IsActiveMemberAsync(factory, AliceId));
    }

    // ---- helpers ----

    private static List<Claim> Admin() => [new(ClaimTypes.Email, AliceEmail), new(ClaimTypes.Role, RoleNames.Admin)];

    private static List<Claim> UserManager() => [new(ClaimTypes.Email, AliceEmail), new("permission", "User:Any:Manage")];

    private static List<Claim> Bob() =>
    [
        new(ClaimTypes.Email, EaContextSeeder.BobEmail),
        new("permission", $"Application:{SeededApplication}:Read")
    ];

    private static void CallAs(CustomWebApplicationDbContextFactory<Program> factory, HttpClient httpClient, List<Claim> claims)
    {
        // CoreLibs registers TestClaims as a singleton the first time the mock handler is built, so a new
        // list assigned later is ignored. Change the same list instead, so a test can switch callers.
        factory.TestClaims ??= [];
        factory.TestClaims.Clear();
        factory.TestClaims.AddRange(claims);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user-token");
    }

    private static SetUserPermissionsRequest Request(params (ResourceType Type, string Key, AccessType Access)[] grants) => new()
    {
        Permissions = grants.Select(g => new RolePermissionGrantDto
        {
            ResourceType = g.Type,
            ResourceKey = g.Key,
            AccessType = g.Access
        }).ToList()
    };

    private static async Task<List<Permission>> GrantsAsync(CustomWebApplicationDbContextFactory<Program> factory, Guid userId)
    {
        var id = new UserId(userId);
        return await factory.GetDbContext<ExternalApplicationsContext>().Permissions.AsNoTracking()
            .Where(p => p.UserId == id)
            .ToListAsync();
    }

    private static async Task<bool> IsActiveMemberAsync(CustomWebApplicationDbContextFactory<Program> factory, Guid userId)
    {
        var id = new UserId(userId);
        return await factory.GetDbContext<ExternalApplicationsContext>().TenantMemberships.AsNoTracking()
            .AnyAsync(m => m.TenantId == TenantId && m.UserId == id && m.IsActive);
    }

    private static async Task AddGrantAsync(
        CustomWebApplicationDbContextFactory<Program> factory,
        Guid userId,
        ResourceType resourceType,
        string resourceKey,
        AccessType accessType,
        Guid? applicationId = null)
    {
        var db = factory.GetDbContext<ExternalApplicationsContext>();
        db.Permissions.Add(new Permission(
            new PermissionId(Guid.NewGuid()),
            new UserId(userId),
            applicationId is { } app ? new ApplicationId(app) : null,
            resourceKey,
            resourceType,
            accessType,
            DateTime.UtcNow,
            new UserId(AliceId)));
        await db.SaveChangesAsync();
    }

    /// <summary>A user, optionally an active member of the test tenant in its Admin or User role.</summary>
    private static async Task<Guid> SeedUserAsync(CustomWebApplicationDbContextFactory<Program> factory, string? membershipRole)
    {
        var db = factory.GetDbContext<ExternalApplicationsContext>();
        var userId = new UserId(Guid.NewGuid());
        var tenantUserRole = await db.Roles.AsNoTracking().FirstAsync(r => r.TenantId == TenantId && r.Name == RoleNames.User);
        db.Users.Add(new User(userId, tenantUserRole.Id!, "Seeded User", $"user-{userId.Value:N}@example.com", DateTime.UtcNow, null, null, null));
        if (membershipRole is not null)
        {
            var role = await db.Roles.AsNoTracking().FirstAsync(r => r.TenantId == TenantId && r.Name == membershipRole);
            db.TenantMemberships.Add(new TenantMembership(new TenantMembershipId(Guid.NewGuid()), TenantId, userId, role.Id!, DateTime.UtcNow));
        }

        await db.SaveChangesAsync();
        return userId.Value;
    }

    /// <summary>A template, version and application owned by another tenant, so outside this tenant's catalogue.</summary>
    private static async Task<(Guid TemplateId, Guid ApplicationId)> SeedOtherTenantApplicationAsync(
        CustomWebApplicationDbContextFactory<Program> factory)
    {
        var db = factory.GetDbContext<ExternalApplicationsContext>();
        var now = DateTime.UtcNow;
        var alice = new UserId(AliceId);
        var templateId = new TemplateId(Guid.NewGuid());
        var versionId = new TemplateVersionId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());
        db.Templates.Add(new Template(templateId, "Other tenant's form", now, alice, isLive: true, tenantId: OtherTenantId));
        db.TemplateVersions.Add(new TemplateVersion(versionId, templateId, "1.0.0", "{}", now, alice, null, null));
        await db.SaveChangesAsync();
        db.Applications.Add(new Domain.Entities.Application(
            applicationId, "OTH-001", versionId, now, new UserId(BobId), ApplicationStatus.InProgress, null, null));
        await db.SaveChangesAsync();
        return (templateId.Value, applicationId.Value);
    }
}
