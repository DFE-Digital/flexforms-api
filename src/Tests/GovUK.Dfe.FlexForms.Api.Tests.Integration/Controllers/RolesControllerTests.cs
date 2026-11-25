using System.Net.Http.Headers;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

/// <summary>
/// Tenant role administration (/roles): Admin and SuperAdmin only. System roles (Admin, User) can't be
/// renamed, deleted or have their permissions replaced; custom roles can, within the tenant's catalogue.
/// </summary>
public class RolesControllerTests
{
    private static readonly Guid TenantId = Guid.Parse(EaContextSeeder.TestTenantId);
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    // ---- list ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task List_ReturnsTheTenantsSystemAndCustomRoles(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var custom = await rolesClient.CreateAsync(new CreateTenantRoleRequest { Name = "Reviewer" });

        var roles = await rolesClient.ListAsync();

        Assert.Contains(roles, r => r.Name == RoleNames.Admin && r.IsSystem);
        Assert.Contains(roles, r => r.Name == RoleNames.User && r.IsSystem);
        Assert.Contains(roles, r => r.RoleId == custom.RoleId && !r.IsSystem);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task EnsureSystemRoles_SeedsAdminAndUserForANewTenant_Once(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        // A new tenant has only config rows. The list endpoint calls this before listing.
        var newTenant = Guid.NewGuid();
        CallAs(factory, httpClient, Admin());
        _ = await rolesClient.ListAsync(); // build the host

        for (var i = 0; i < 2; i++)
        {
            using var scope = factory.Services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<ITenantRoleService>();
            await roles.EnsureSystemRolesAsync(newTenant);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        var seeded = await factory.GetDbContext<ExternalApplicationsContext>().Roles.AsNoTracking()
            .Where(r => r.TenantId == newTenant)
            .ToListAsync();
        Assert.Equal(
            [RoleNames.Admin, RoleNames.User],
            seeded.Select(r => r.Name).Order(StringComparer.Ordinal));
        Assert.All(seeded, r => Assert.True(r.IsSystem));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RoleEndpoints_AreRefusedForAUserManagerWhoIsNotAnAdmin(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, [new(ClaimTypes.Email, "alice@example.com"), new("permission", "User:Any:Manage")]);

        var list = await ApiErrorAsync(() => rolesClient.ListAsync());
        var create = await ApiErrorAsync(() => rolesClient.CreateFromTemplateAsync(new CreateTenantRoleFromTemplateRequest(RoleTemplates.CaseworkerKey)));

        Assert.Equal(403, list.Status);
        Assert.Equal(403, create.Status);
    }

    // ---- create from a preset ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateFromTemplate_CopiesThePresetGrants(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());

        foreach (var preset in RoleTemplates.All)
        {
            var role = await rolesClient.CreateFromTemplateAsync(new CreateTenantRoleFromTemplateRequest(preset.Key));
            var grants = await rolesClient.GetPermissionsAsync(role.RoleId);

            Assert.Equal(preset.DefaultRoleName, role.Name);
            Assert.False(role.IsSystem);
            Assert.Equal(
                preset.Grants.Select(g => (g.ResourceType, g.ResourceKey, g.AccessType)).Order(),
                grants.Select(g => (g.ResourceType, g.ResourceKey, g.AccessType)).Order());
        }
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateFromTemplate_RejectsAnUnknownPresetAndASecondCopy(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        await rolesClient.CreateFromTemplateAsync(new CreateTenantRoleFromTemplateRequest(RoleTemplates.CaseworkerKey));

        var unknown = await ApiErrorAsync(() => rolesClient.CreateFromTemplateAsync(new CreateTenantRoleFromTemplateRequest("Auditor")));
        var duplicate = await ApiErrorAsync(() => rolesClient.CreateFromTemplateAsync(new CreateTenantRoleFromTemplateRequest(RoleTemplates.CaseworkerKey)));

        Assert.Equal(400, unknown.Status);
        Assert.Contains("Unknown role template 'Auditor'", unknown.Message);
        Assert.Equal(400, duplicate.Status);
        Assert.Contains("already exists", duplicate.Message);
    }

    // ---- rename ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Rename_ChangesACustomRolesName(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var role = await rolesClient.CreateAsync(new CreateTenantRoleRequest { Name = "Reviewer" });

        await rolesClient.RenameAsync(role.RoleId, new RenameTenantRoleRequest { Name = "Senior reviewer" });

        Assert.Equal("Senior reviewer", (await RoleAsync(factory, role.RoleId)).Name);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Rename_RefusesADuplicateNameASystemRoleAndAnUnknownRole(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var reviewer = await rolesClient.CreateAsync(new CreateTenantRoleRequest { Name = "Reviewer" });
        await rolesClient.CreateAsync(new CreateTenantRoleRequest { Name = "Approver" });
        var admin = await SystemRoleIdAsync(factory, RoleNames.Admin);

        var duplicate = await ApiErrorAsync(() => rolesClient.RenameAsync(reviewer.RoleId, new RenameTenantRoleRequest { Name = "Approver" }));
        var system = await ApiErrorAsync(() => rolesClient.RenameAsync(admin, new RenameTenantRoleRequest { Name = "Owner" }));
        var unknown = await ApiErrorAsync(() => rolesClient.RenameAsync(Guid.NewGuid(), new RenameTenantRoleRequest { Name = "Anything" }));

        Assert.Equal(400, duplicate.Status);
        Assert.Contains("already exists", duplicate.Message);
        Assert.Equal(400, system.Status);
        Assert.Contains("cannot be renamed", system.Message);
        Assert.Equal(404, unknown.Status);
        Assert.Equal("Reviewer", (await RoleAsync(factory, reviewer.RoleId)).Name);
        Assert.Equal(RoleNames.Admin, (await RoleAsync(factory, admin)).Name);
    }

    // ---- delete ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Delete_RemovesAnUnusedCustomRole(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var role = await rolesClient.CreateFromTemplateAsync(new CreateTenantRoleFromTemplateRequest(RoleTemplates.CaseworkerKey));

        await rolesClient.DeleteAsync(role.RoleId);

        var db = factory.GetDbContext<ExternalApplicationsContext>();
        var roleId = new RoleId(role.RoleId);
        Assert.False(await db.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task Delete_RefusesARoleInUseASystemRoleAndAnUnknownRole(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var inUse = await rolesClient.CreateAsync(new CreateTenantRoleRequest { Name = "Reviewer" });
        await AssignMembershipAsync(factory, inUse.RoleId);
        var user = await SystemRoleIdAsync(factory, RoleNames.User);

        var assigned = await ApiErrorAsync(() => rolesClient.DeleteAsync(inUse.RoleId));
        var system = await ApiErrorAsync(() => rolesClient.DeleteAsync(user));
        var unknown = await ApiErrorAsync(() => rolesClient.DeleteAsync(Guid.NewGuid()));

        Assert.Equal(400, assigned.Status);
        Assert.Contains("while users are assigned to it", assigned.Message);
        Assert.Equal(400, system.Status);
        Assert.Contains("cannot be deleted", system.Message);
        Assert.Equal(404, unknown.Status);
        Assert.NotNull(await RoleAsync(factory, inUse.RoleId));
    }

    // ---- set permissions ----

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetPermissions_ReplacesACustomRolesGrants(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var role = await rolesClient.CreateFromTemplateAsync(new CreateTenantRoleFromTemplateRequest(RoleTemplates.CaseworkerKey));

        var saved = await rolesClient.SetPermissionsAsync(role.RoleId, Request(
            (ResourceType.Template, EaContextSeeder.TemplateId, AccessType.Read),
            (ResourceType.Application, EaContextSeeder.ApplicationId, AccessType.Read),
            (ResourceType.Notifications, "bob@example.com", AccessType.Read)));

        (ResourceType, string, AccessType)[] expected =
        [
            (ResourceType.Application, EaContextSeeder.ApplicationId, AccessType.Read),
            (ResourceType.Notifications, "bob@example.com", AccessType.Read),
            (ResourceType.Template, EaContextSeeder.TemplateId, AccessType.Read)
        ];
        Assert.Equal(expected.Order(), saved.Select(g => (g.ResourceType, g.ResourceKey, g.AccessType)).Order());
        // The Caseworker preset's tenant-wide grants were replaced, not kept.
        Assert.DoesNotContain(await rolesClient.GetPermissionsAsync(role.RoleId), g => g.ResourceKey == PermissionConstants.AnyResourceKey);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetPermissions_RefusesResourcesOutsideTheTenantAndUnknownEmails(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        var other = await SeedOtherTenantApplicationAsync(factory);
        CallAs(factory, httpClient, Admin());
        var role = await rolesClient.CreateAsync(new CreateTenantRoleRequest { Name = "Reviewer" });
        var cases = new (ResourceType Type, string Key, string Message)[]
        {
            (ResourceType.Template, other.TemplateId.ToString(), "was not found in the current tenant"),
            (ResourceType.Application, other.ApplicationId.ToString(), "does not belong to the current tenant"),
            (ResourceType.Application, Guid.NewGuid().ToString(), "was not found"),
            (ResourceType.User, "nobody@example.com", "'nobody@example.com' was not found"),
        };

        foreach (var (type, key, message) in cases)
        {
            var ex = await ApiErrorAsync(() => rolesClient.SetPermissionsAsync(role.RoleId, Request((type, key, AccessType.Read))));

            Assert.Equal(400, ex.Status);
            Assert.Contains(message, ex.Message);
        }

        Assert.Empty(await rolesClient.GetPermissionsAsync(role.RoleId));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SetPermissions_RefusesASystemRoleAndAnUnknownRole(
        CustomWebApplicationDbContextFactory<Program> factory, IRolesClient rolesClient, HttpClient httpClient)
    {
        CallAs(factory, httpClient, Admin());
        var user = await SystemRoleIdAsync(factory, RoleNames.User);
        var request = Request((ResourceType.Template, EaContextSeeder.TemplateId, AccessType.Read));

        var system = await ApiErrorAsync(() => rolesClient.SetPermissionsAsync(user, request));
        var unknown = await ApiErrorAsync(() => rolesClient.SetPermissionsAsync(Guid.NewGuid(), request));

        Assert.Equal(400, system.Status);
        Assert.Contains("System role permissions cannot be replaced", system.Message);
        Assert.Equal(404, unknown.Status);
    }

    // ---- helpers ----

    /// <summary>
    /// The roles endpoints answer some errors with a typed body and some without, so read either.
    /// </summary>
    private static async Task<(int Status, string Message)> ApiErrorAsync(Func<Task> call)
    {
        var ex = await Assert.ThrowsAnyAsync<ExternalApplicationsException>(call);
        var message = ex is ExternalApplicationsException<ExceptionResponse> typed
            ? typed.Result?.Message ?? ""
            : ex.Response ?? "";
        return (ex.StatusCode, message);
    }

    private static List<Claim> Admin() => [new(ClaimTypes.Email, "alice@example.com"), new(ClaimTypes.Role, RoleNames.Admin)];

    private static void CallAs(CustomWebApplicationDbContextFactory<Program> factory, HttpClient httpClient, List<Claim> claims)
    {
        // CoreLibs reads TestClaims once, when the mock handler is first built; change the same list.
        factory.TestClaims ??= [];
        factory.TestClaims.Clear();
        factory.TestClaims.AddRange(claims);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user-token");
    }

    private static SetRolePermissionsRequest Request(params (ResourceType Type, string Key, AccessType Access)[] grants) => new()
    {
        Permissions = grants.Select(g => new RolePermissionGrantDto
        {
            ResourceType = g.Type,
            ResourceKey = g.Key,
            AccessType = g.Access
        }).ToList()
    };

    private static async Task<Role> RoleAsync(CustomWebApplicationDbContextFactory<Program> factory, Guid roleId)
    {
        var id = new RoleId(roleId);
        return await factory.GetDbContext<ExternalApplicationsContext>().Roles.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private static async Task<Guid> SystemRoleIdAsync(CustomWebApplicationDbContextFactory<Program> factory, string name)
    {
        var role = await factory.GetDbContext<ExternalApplicationsContext>().Roles.AsNoTracking()
            .SingleAsync(r => r.TenantId == TenantId && r.Name == name);
        return role.Id!.Value;
    }

    private static async Task AssignMembershipAsync(CustomWebApplicationDbContextFactory<Program> factory, Guid roleId)
    {
        var db = factory.GetDbContext<ExternalApplicationsContext>();
        var userId = new UserId(Guid.NewGuid());
        db.Users.Add(new User(userId, new RoleId(roleId), "Reviewer", $"reviewer-{userId.Value:N}@example.com", DateTime.UtcNow, null, null, null));
        db.TenantMemberships.Add(new TenantMembership(new TenantMembershipId(Guid.NewGuid()), TenantId, userId, new RoleId(roleId), DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid TemplateId, Guid ApplicationId)> SeedOtherTenantApplicationAsync(
        CustomWebApplicationDbContextFactory<Program> factory)
    {
        var db = factory.GetDbContext<ExternalApplicationsContext>();
        var now = DateTime.UtcNow;
        var alice = new UserId(Guid.Parse(EaContextSeeder.AliceId));
        var templateId = new TemplateId(Guid.NewGuid());
        var versionId = new TemplateVersionId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());
        db.Templates.Add(new Template(templateId, "Other tenant's form", now, alice, isLive: true, tenantId: OtherTenantId));
        db.TemplateVersions.Add(new TemplateVersion(versionId, templateId, "1.0.0", "{}", now, alice, null, null));
        await db.SaveChangesAsync();
        db.Applications.Add(new Domain.Entities.Application(
            applicationId, "OTH-001", versionId, now, new UserId(Guid.Parse(EaContextSeeder.BobId)), ApplicationStatus.InProgress, null, null));
        await db.SaveChangesAsync();
        return (templateId.Value, applicationId.Value);
    }
}
