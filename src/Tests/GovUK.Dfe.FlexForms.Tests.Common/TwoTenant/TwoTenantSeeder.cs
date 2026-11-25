using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Utils.File;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;

/// <summary>
/// Seeds one shared EA database with the same shape of data for <see cref="TestTenant.A"/> and
/// <see cref="TestTenant.B"/> (one member per role, a self-registered member, a member whose only template
/// grant is Manage on the tenant's form, and three spare members to target with user-management writes), and <see cref="BothTenantsEmail"/> (Admin in A, User in B) and a platform
/// <see cref="SuperAdminEmail"/> with no membership. Users are global rows; roles, memberships, templates
/// and applications belong to one tenant, as in production.
/// </summary>
public static class TwoTenantSeeder
{
    public const string BothTenantsEmail = "both.tenants@example.com";
    public const string SuperAdminEmail = "superadmin@example.com";
    public static readonly Guid BothTenantsUserId = Guid.Parse("00000000-0000-4000-8000-0000000000b0");
    public static readonly Guid SuperAdminId = Guid.Parse("00000000-0000-4000-8000-0000000000a0");

    public static void Seed(ExternalApplicationsContext ctx)
    {
        var now = DateTime.UtcNow;
        var globalAdmin = new Role(new RoleId(RoleConstants.AdminRoleId), RoleNames.Admin);
        var globalUser = new Role(new RoleId(RoleConstants.UserRoleId), RoleNames.User);
        ctx.Roles.AddRange(globalAdmin, globalUser);

        var superAdmin = new User(new UserId(SuperAdminId), globalAdmin.Id, "Platform SuperAdmin", SuperAdminEmail, now, null, null, null);
        var both = new User(new UserId(BothTenantsUserId), globalUser.Id, "Both Tenants", BothTenantsEmail, now, null, null, null);
        ctx.Users.AddRange(superAdmin, both);
        ctx.SaveChanges();

        foreach (var tenant in new[] { TestTenant.A, TestTenant.B })
        {
            SeedTenant(ctx, tenant, globalUser.Id!, now);
        }

        ctx.TenantMemberships.AddRange(
            Membership(TestTenant.A.Id, both.Id!, TestTenant.A.AdminRoleId, now),
            Membership(TestTenant.B.Id, both.Id!, TestTenant.B.UserRoleId, now));
        ctx.SaveChanges();
    }

    private static void SeedTenant(ExternalApplicationsContext ctx, TestTenant tenant, RoleId globalUserRoleId, DateTime now)
    {
        var admin = new Role(new RoleId(tenant.AdminRoleId), RoleNames.Admin, tenant.Id, isSystem: true);
        var user = new Role(new RoleId(tenant.UserRoleId), RoleNames.User, tenant.Id, isSystem: true);
        var caseworker = new Role(new RoleId(tenant.CaseworkerRoleId), RoleTemplates.CaseworkerKey, tenant.Id);
        var templateManager = new Role(new RoleId(tenant.TemplateManagerRoleId), RoleTemplates.TemplateManagerKey, tenant.Id);
        var userManager = new Role(new RoleId(tenant.UserManagerRoleId), "UserManager", tenant.Id);
        ctx.Roles.AddRange(admin, user, caseworker, templateManager, userManager);
        ctx.SaveChanges();

        foreach (var grant in RoleTemplates.Get(RoleTemplates.CaseworkerKey)!.Grants)
            ctx.RolePermissions.Add(RoleGrant(caseworker, grant.ResourceType, grant.AccessType, now));
        foreach (var grant in RoleTemplates.Get(RoleTemplates.TemplateManagerKey)!.Grants)
            ctx.RolePermissions.Add(RoleGrant(templateManager, grant.ResourceType, grant.AccessType, now));
        ctx.RolePermissions.Add(RoleGrant(userManager, ResourceType.User, AccessType.Manage, now));

        var members = new (Guid Id, string Email, string Name, Role Role)[]
        {
            (tenant.OwnerId, tenant.OwnerEmail, $"{tenant.Name} Owner", user),
            (tenant.AdminId, tenant.AdminEmail, $"{tenant.Name} Admin", admin),
            (tenant.CaseworkerId, tenant.CaseworkerEmail, $"{tenant.Name} Caseworker", caseworker),
            (tenant.TemplateManagerId, tenant.TemplateManagerEmail, $"{tenant.Name} Template Manager", templateManager),
            (tenant.UserManagerId, tenant.UserManagerEmail, $"{tenant.Name} User Manager", userManager),
            (tenant.ScopedTemplateManagerId, tenant.ScopedTemplateManagerEmail, $"{tenant.Name} Form Manager", user),
            (tenant.SpareMemberId(1), tenant.SpareMemberEmail(1), $"{tenant.Name} Spare 1", user),
            (tenant.SpareMemberId(2), tenant.SpareMemberEmail(2), $"{tenant.Name} Spare 2", user),
            (tenant.SpareMemberId(3), tenant.SpareMemberEmail(3), $"{tenant.Name} Spare 3", user),
        };
        // Everyone starts as production users do: created by the self-registration factory call, which grants
        // reading their own user record and their notifications in this tenant. The owner registered on the
        // tenant's form, so also holds Read and Write on it. Roles and other grants come on top.
        var templateId = new TemplateId(tenant.TemplateId);
        var factory = new UserFactory();
        foreach (var member in members)
        {
            var formId = member.Id == tenant.OwnerId ? templateId : null;
            ctx.Users.Add(factory.CreateUser(new UserId(member.Id), globalUserRoleId, member.Name, member.Email, formId, now, tenant.Id));
            ctx.TenantMemberships.Add(Membership(tenant.Id, new UserId(member.Id), member.Role.Id!.Value, now));
        }
        ctx.SaveChanges();

        var ownerId = new UserId(tenant.OwnerId);
        var adminId = new UserId(tenant.AdminId);
        var versionId = new TemplateVersionId(tenant.TemplateVersionId);
        ctx.Templates.Add(new Template(templateId, $"{tenant.Name} form", now, adminId, isLive: true, tenantId: tenant.Id));
        ctx.TemplateVersions.Add(new TemplateVersion(
            versionId,
            templateId,
            versionNumber: "1.0.0",
            jsonSchema: """{ "type": "object", "properties": { "name": { "type": "string" } } }""",
            createdOn: now,
            createdBy: adminId,
            lastModifiedOn: null,
            lastModifiedBy: null));
        ctx.SaveChanges();

        // A member who self-registered on the form and holds nothing else.
        var selfRegistered = factory.CreateUser(
            new UserId(tenant.MemberId), globalUserRoleId, $"{tenant.Name} Member", tenant.MemberEmail, templateId, now, tenant.Id);
        ctx.Users.Add(selfRegistered);
        ctx.TenantMemberships.Add(Membership(tenant.Id, selfRegistered.Id!, tenant.UserRoleId, now));
        ctx.SaveChanges();

        var applicationId = new ApplicationId(tenant.ApplicationId);
        ctx.Applications.Add(new Domain.Entities.Application(
            applicationId,
            applicationReference: tenant.ApplicationReference,
            templateVersionId: versionId,
            createdOn: now,
            createdBy: ownerId,
            status: ApplicationStatus.InProgress,
            lastModifiedOn: null,
            lastModifiedBy: null));
        ctx.SaveChanges();

        ctx.Files.Add(new File(
            new FileId(tenant.FileId),
            applicationId,
            name: "evidence.pdf",
            description: null,
            originalFileName: "evidence.pdf",
            fileName: FileNameHasher.HashFileName("evidence.pdf"),
            path: tenant.ApplicationReference,
            uploadedOn: now,
            uploadedBy: ownerId,
            fileSize: 0));
        ctx.ApplicationResponses.Add(new ApplicationResponse(
            new ResponseId(tenant.ResponseId),
            applicationId,
            responseBody: $$"""{"name":"{{tenant.Name}} applicant","evidence":"{{tenant.FileId}}"}""",
            createdOn: now,
            createdBy: ownerId,
            lastModifiedOn: null,
            lastModifiedBy: null));

        // What creating the application grants its owner.
        var key = applicationId.Value.ToString();
        ctx.Permissions.AddRange(
            Grant(ownerId, applicationId, key, ResourceType.Application, AccessType.Read, adminId, now),
            Grant(ownerId, applicationId, key, ResourceType.Application, AccessType.Write, adminId, now),
            Grant(ownerId, applicationId, key, ResourceType.ApplicationFiles, AccessType.Read, adminId, now),
            Grant(ownerId, applicationId, key, ResourceType.ApplicationFiles, AccessType.Write, adminId, now),
            Grant(ownerId, applicationId, key, ResourceType.ApplicationFiles, AccessType.Delete, adminId, now),
            Grant(new UserId(tenant.ScopedTemplateManagerId), null, templateId.Value.ToString(), ResourceType.Template, AccessType.Manage, adminId, now));
        ctx.SaveChanges();
    }

    private static TenantMembership Membership(Guid tenantId, UserId userId, Guid roleId, DateTime now) =>
        new(new TenantMembershipId(Guid.NewGuid()), tenantId, userId, new RoleId(roleId), now);

    private static RolePermission RoleGrant(Role role, ResourceType type, AccessType access, DateTime now) =>
        new(new RolePermissionId(Guid.NewGuid()), role.Id!, PermissionConstants.AnyResourceKey, type, access, now);

    private static Permission Grant(
        UserId userId, ApplicationId? applicationId, string key, ResourceType type, AccessType access, UserId grantedBy, DateTime now) =>
        new(new PermissionId(Guid.NewGuid()), userId, applicationId, key, type, access, now, grantedBy);
}
