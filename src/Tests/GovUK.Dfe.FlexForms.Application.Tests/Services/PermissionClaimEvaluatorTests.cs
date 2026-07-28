using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class PermissionClaimEvaluatorTests
{
    [Fact]
    public void CanReadAllApplications_ReturnsTrue_ForTenantWideApplicationReadClaim()
    {
        var user = CreateUserWithPermissionClaims("Application:Any:Read");
        Assert.True(PermissionClaimEvaluator.CanReadAllApplications(user));
    }

    [Fact]
    public void CanManageTemplates_ReturnsTrue_ForTemplateAnyManageClaim()
    {
        var user = CreateUserWithPermissionClaims("Template:Any:Manage");
        Assert.True(PermissionClaimEvaluator.CanManageTemplates(user));
    }

    [Fact]
    public void CanManageTemplates_ReturnsFalse_ForTemplateAnyWriteClaim()
    {
        var user = CreateUserWithPermissionClaims("Template:Any:Write");
        Assert.False(PermissionClaimEvaluator.CanManageTemplates(user));
    }

    [Fact]
    public void CanManageTemplate_ReturnsTrue_ForSpecificTemplateManageClaim()
    {
        var templateId = Guid.NewGuid().ToString();
        var user = CreateUserWithPermissionClaims($"Template:{templateId}:Manage");
        Assert.True(PermissionClaimEvaluator.CanManageTemplate(user, templateId));
        Assert.False(PermissionClaimEvaluator.CanManageTemplates(user));
    }

    [Fact]
    public void CanWriteApplication_ReturnsFalse_WithoutWriteClaim()
    {
        var user = CreateUserWithPermissionClaims("Application:Any:Read");
        Assert.False(PermissionClaimEvaluator.CanWriteApplication(user, Guid.NewGuid().ToString()));
    }

    [Fact]
    public void CanReadApplication_ReturnsTrue_ForTenantWideApplicationReadClaim()
    {
        var user = CreateUserWithPermissionClaims("Application:Any:Read");
        Assert.True(PermissionClaimEvaluator.CanReadApplication(user, Guid.NewGuid().ToString()));
    }

    [Fact]
    public void CanReadApplication_ReturnsFalse_ForUserWithoutMatchingClaim()
    {
        var user = CreateUserWithPermissionClaims("ApplicationFiles:Any:Read");
        Assert.False(PermissionClaimEvaluator.CanReadApplication(user, Guid.NewGuid().ToString()));
    }

    [Fact]
    public void CanReadApplication_ReturnsTrue_ForStandardUserWithExplicitApplicationReadClaim()
    {
        var applicationId = Guid.NewGuid().ToString();
        var user = CreateUserWithPermissionClaims($"Application:{applicationId}:Read");
        Assert.True(PermissionClaimEvaluator.CanReadApplication(user, applicationId));
    }

    [Fact]
    public void CanReadAllApplications_ReturnsFalse_WithoutTenantWideOrAdminAccess()
    {
        var applicationId = Guid.NewGuid().ToString();
        var user = CreateUserWithPermissionClaims($"Application:{applicationId}:Read");
        Assert.False(PermissionClaimEvaluator.CanReadAllApplications(user));
    }

    [Fact]
    public void HasAnyExplicitPermissionClaim_ReturnsFalse_WhenOnlyWildcardClaimExists()
    {
        var user = CreateUserWithPermissionClaims("Application:Any:Read");
        Assert.False(PermissionClaimEvaluator.HasAnyExplicitPermissionClaim(user, ResourceType.Application, AccessType.Read));
    }

    [Fact]
    public void HasAnyExplicitPermissionClaim_ReturnsTrue_WhenExplicitApplicationClaimExists()
    {
        var user = CreateUserWithPermissionClaims($"Application:{Guid.NewGuid()}:Read");
        Assert.True(PermissionClaimEvaluator.HasAnyExplicitPermissionClaim(user, ResourceType.Application, AccessType.Read));
    }

    [Fact]
    public void ApplicationAccessResolver_ReturnsAllApplications_ForAdminRole()
    {
        var user = CreateAdminUser();
        var scope = ApplicationAccessResolver.Resolve(user);
        Assert.Equal(ApplicationAccessResolver.AccessMode.AllApplicationsInTenant, scope.Mode);
    }

    [Fact]
    public void ApplicationAccessResolver_ReturnsAllApplications_ForTenantWideApplicationReadGrant()
    {
        var user = CreateUserWithTenantWideApplicationRead();
        var scope = ApplicationAccessResolver.Resolve(user);
        Assert.Equal(ApplicationAccessResolver.AccessMode.AllApplicationsInTenant, scope.Mode);
    }

    [Fact]
    public void ApplicationAccessResolver_ReturnsSpecificApplications_ForStandardUserWithExplicitAppGrant()
    {
        var userId = new UserId(Guid.NewGuid());
        var ownedApplicationId = new ApplicationId(Guid.NewGuid());
        var user = new User(
            userId,
            new RoleId(RoleConstants.UserRoleId),
            "Applicant",
            "user@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        user.GetType().GetProperty(nameof(User.Role))!.SetValue(user,
            new Role(new RoleId(RoleConstants.UserRoleId), RoleNames.User));

        var permissions = user.GetType()
            .GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        permissions.SetValue(user, new List<Permission>
        {
            new(
                new PermissionId(Guid.NewGuid()),
                userId,
                ownedApplicationId,
                ownedApplicationId.Value.ToString(),
                ResourceType.Application,
                AccessType.Read,
                DateTime.UtcNow,
                userId)
        });

        var scope = ApplicationAccessResolver.Resolve(user);

        Assert.Equal(ApplicationAccessResolver.AccessMode.SpecificApplicationIds, scope.Mode);
        Assert.Single(scope.ApplicationIds);
        Assert.Equal(ownedApplicationId, scope.ApplicationIds.First());
        Assert.Empty(scope.TemplateIds);
    }

    [Fact]
    public void ApplicationAccessResolver_ReturnsEmpty_ForUserWithoutApplicationGrants()
    {
        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            "Applicant",
            "user@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        user.GetType().GetProperty(nameof(User.Role))!.SetValue(user,
            new Role(new RoleId(RoleConstants.UserRoleId), RoleNames.User));

        var scope = ApplicationAccessResolver.Resolve(user);

        Assert.Equal(ApplicationAccessResolver.AccessMode.SpecificApplicationIds, scope.Mode);
        Assert.Empty(scope.ApplicationIds);
        Assert.Empty(scope.TemplateIds);
    }

    [Fact]
    public void CanListAllApplicationsForTemplate_ReturnsTrue_ForAdmin()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var user = CreateAdminUser();
        Assert.True(ApplicationAccessResolver.CanListAllApplicationsForTemplate(user, templateId));
    }

    [Fact]
    public void CanListAllApplicationsForTemplate_ReturnsTrue_WhenUserHasTenantWideApplicationRead()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var user = CreateUserWithTenantWideApplicationRead();
        Assert.True(ApplicationAccessResolver.CanListAllApplicationsForTemplate(user, templateId));
    }

    [Fact]
    public void CanListAllApplicationsForTemplate_ReturnsFalse_ForStandardUserWithApplicationOnlyAccess()
    {
        var userId = new UserId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());
        var user = new User(
            userId,
            new RoleId(RoleConstants.UserRoleId),
            "Standard User",
            "user@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        user.GetType().GetProperty(nameof(User.Role))!.SetValue(user,
            new Role(new RoleId(RoleConstants.UserRoleId), RoleNames.User));

        var permissions = user.GetType()
            .GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        permissions.SetValue(user, new List<Permission>
        {
            new(
                new PermissionId(Guid.NewGuid()),
                userId,
                applicationId,
                "Application:Read",
                ResourceType.Application,
                AccessType.Read,
                DateTime.UtcNow,
                userId)
        });

        Assert.False(ApplicationAccessResolver.CanListAllApplicationsForTemplate(user, new TemplateId(Guid.NewGuid())));
    }

    [Fact]
    public void IsInteractiveTenantAdmin_ReturnsTrue_ForAdminUserWithEmail()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, RoleNames.Admin),
            new Claim(ClaimTypes.Email, "admin@example.com")
        ], "Test"));

        Assert.True(PermissionClaimEvaluator.IsInteractiveTenantAdmin(user));
    }

    [Fact]
    public void IsInteractiveTenantAdmin_ReturnsFalse_ForServicePrincipalWithAdminRole()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, RoleNames.Admin),
            new Claim(TenantAuthClaimTypes.IsService, "true"),
            new Claim(ClaimTypes.Email, "svc@apps.local")
        ], "Test"));

        Assert.False(PermissionClaimEvaluator.IsInteractiveTenantAdmin(user));
    }

    private static User CreateAdminUser()
    {
        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.AdminRoleId),
            "Admin User",
            "admin@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        user.GetType().GetProperty(nameof(User.Role))!.SetValue(user,
            new Role(new RoleId(RoleConstants.AdminRoleId), RoleNames.Admin));

        return user;
    }

    private static User CreateUserWithTenantWideApplicationRead()
    {
        var userId = new UserId(Guid.NewGuid());
        var user = new User(
            userId,
            new RoleId(RoleConstants.UserRoleId),
            "Reviewer",
            "reviewer@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        user.GetType().GetProperty(nameof(User.Role))!.SetValue(user,
            new Role(new RoleId(RoleConstants.UserRoleId), RoleNames.User));

        var permissions = user.GetType()
            .GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        permissions.SetValue(user, new List<Permission>
        {
            new(
                new PermissionId(Guid.NewGuid()),
                userId,
                null,
                PermissionConstants.AnyResourceKey,
                ResourceType.Application,
                AccessType.Read,
                DateTime.UtcNow,
                userId)
        });

        return user;
    }

    private static ClaimsPrincipal CreateUserWithPermissionClaims(params string[] permissionValues)
    {
        var claims = permissionValues.Select(v => new Claim(PermissionClaimEvaluator.PermissionClaimType, v));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
