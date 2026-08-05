using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

public class AssignUserRoleCommandHandlerTests
{
    private static ITenantContextAccessor CreateTenantContext()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var tenant = new TenantConfiguration(Guid.NewGuid(), "Test", config, Array.Empty<string>());
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);
        return accessor;
    }

    private static ITenantMembershipService CreateMembershipService()
    {
        var service = Substitute.For<ITenantMembershipService>();
        service.UpsertMembershipAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var tenantId = ci.ArgAt<Guid>(0);
                var userId = ci.ArgAt<UserId>(1);
                var roleName = ci.ArgAt<string>(2);
                var role = new Role(new RoleId(Guid.NewGuid()), roleName, tenantId, true);
                return new TenantMembership(
                    new TenantMembershipId(Guid.NewGuid()),
                    tenantId,
                    userId,
                    role.Id!,
                    DateTime.UtcNow);
            });
        return service;
    }

    private static ITenantRoleService CreateTenantRoleService(Role? customRole = null)
    {
        var service = Substitute.For<ITenantRoleService>();
        service.GetByNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customRole);
        service.GetOrCreateTenantRoleAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var tenantId = ci.ArgAt<Guid>(0);
                var roleName = ci.ArgAt<string>(1);
                return new Role(new RoleId(Guid.NewGuid()), roleName, tenantId, true);
            });
        return service;
    }

    private static AssignUserRoleCommandHandler CreateHandler(
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor,
        ITenantRoleService? tenantRoleService = null,
        IUserFactory? userFactory = null,
        IUserCacheInvalidator? userCacheInvalidator = null)
    {
        return new AssignUserRoleCommandHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            CreateTenantContext(),
            CreateMembershipService(),
            tenantRoleService ?? CreateTenantRoleService(),
            userFactory ?? Substitute.For<IUserFactory>(),
            httpContextAccessor,
            userCacheInvalidator ?? Substitute.For<IUserCacheInvalidator>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldForbid_WhenCallerIsNotAdmin(
        string email,
        string name,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(false);

        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor);

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, RoleNames.User, [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldFail_WhenCustomRoleDoesNotExist(
        string email,
        string name,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor,
            CreateTenantRoleService(customRole: null));

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, "CaseReviewer", [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("was not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldFail_WhenRoleIsSuperAdmin(
        string email,
        string name,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor);

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, RoleNames.SuperAdmin, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("reserved for platform", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldCreateUser_WhenRoleIsAdmin(
        string adminEmail,
        string email,
        string name,
        UserId adminUserId,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisioner provisioner,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var grantedOn = DateTime.UtcNow;
        var adminUser = new User(
            adminUserId,
            new RoleId(RoleConstants.AdminRoleId),
            "Admin",
            adminEmail,
            grantedOn,
            null,
            null,
            null);

        var createdUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.AdminRoleId),
            name,
            email,
            grantedOn,
            adminUserId,
            null,
            null);

        var users = new List<User> { adminUser }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);
        SetupHttpContext(httpContextAccessor, adminEmail);

        provisioner.RoleName.Returns(RoleNames.Admin);
        provisioner.RequiresTemplateIds.Returns(false);
        provisioner.CreateUser(Arg.Any<RoleAssignmentRequest>()).Returns(createdUser);
        roleProvisionerRegistry.GetProvisioner(RoleNames.Admin).Returns(provisioner);

        var tenantRoleService = CreateTenantRoleService();
        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor,
            tenantRoleService);

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, RoleNames.Admin, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(createdUser.Id!.Value, result.Value!.UserId);
        Assert.Contains(RoleNames.Admin, result.Value.Authorization!.Roles!);

        await tenantRoleService.Received().EnsureSystemRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        provisioner.Received(1).CreateUser(Arg.Is<RoleAssignmentRequest>(r =>
            r.TenantRoleId != null
            && !RoleNames.IsPlatformSuperAdminRoleId(r.TenantRoleId.Value)));
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldCreateUser_WhenUserDoesNotExist(
        string adminEmail,
        string email,
        string name,
        UserId adminUserId,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisioner provisioner,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var templateId = Guid.NewGuid();
        var grantedOn = DateTime.UtcNow;

        var adminUser = new User(
            adminUserId,
            new RoleId(RoleConstants.AdminRoleId),
            "Admin",
            adminEmail,
            grantedOn,
            null,
            null,
            null);

        var createdUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            grantedOn,
            adminUserId,
            null,
            null);

        var users = new List<User> { adminUser }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        SetupHttpContext(httpContextAccessor, adminEmail);

        provisioner.RoleName.Returns(RoleNames.User);
        provisioner.RequiresTemplateIds.Returns(true);
        provisioner.CreateUser(Arg.Any<RoleAssignmentRequest>()).Returns(createdUser);

        roleProvisionerRegistry.GetProvisioner(RoleNames.User).Returns(provisioner);

        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor);

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, RoleNames.User, [templateId]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(createdUser.Id!.Value, result.Value!.UserId);
        Assert.Contains(RoleNames.User, result.Value.Authorization!.Roles!);

        await userRepo.Received(1).AddAsync(createdUser, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        provisioner.Received(1).CreateUser(Arg.Any<RoleAssignmentRequest>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldAssignCustomRole_WhenRoleExists(
        string adminEmail,
        string email,
        string name,
        UserId adminUserId,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IUserFactory userFactory,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var grantedOn = DateTime.UtcNow;
        var customRoleName = "CaseReviewer";
        var tenantId = Guid.NewGuid();
        var customRole = Role.CreateForTenant(tenantId, customRoleName, isSystem: false);

        var adminUser = new User(
            adminUserId,
            new RoleId(RoleConstants.AdminRoleId),
            "Admin",
            adminEmail,
            grantedOn,
            null,
            null,
            null);

        var createdUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            grantedOn,
            adminUserId,
            null,
            null);

        var users = new List<User> { adminUser }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);
        SetupHttpContext(httpContextAccessor, adminEmail);

        userFactory.CreateUser(
                Arg.Any<UserId>(),
                Arg.Any<RoleId>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TemplateId?>(),
                Arg.Any<DateTime?>())
            .Returns(createdUser);

        var tenantRoleService = CreateTenantRoleService(customRole);
        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor,
            tenantRoleService,
            userFactory);

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, customRoleName, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(customRoleName, result.Value!.Authorization!.Roles!);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        roleProvisionerRegistry.DidNotReceive().GetProvisioner(Arg.Any<string>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldAssignRoleToExistingUser(
        string adminEmail,
        string email,
        string name,
        UserId adminUserId,
        UserId existingUserId,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisioner provisioner,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var templateId = Guid.NewGuid();
        var grantedOn = DateTime.UtcNow;

        var adminUser = new User(
            adminUserId,
            new RoleId(RoleConstants.AdminRoleId),
            "Admin",
            adminEmail,
            grantedOn,
            null,
            null,
            null);

        var existingUser = new User(
            existingUserId,
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            grantedOn,
            null,
            null,
            null);

        var users = new List<User> { adminUser, existingUser }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        SetupHttpContext(httpContextAccessor, adminEmail);

        provisioner.RoleName.Returns(RoleNames.User);
        provisioner.RequiresTemplateIds.Returns(true);

        roleProvisionerRegistry.GetProvisioner(RoleNames.User).Returns(provisioner);

        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor);

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, RoleNames.User, [templateId]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingUserId.Value, result.Value!.UserId);

        provisioner.Received(1).AssignToExistingUser(existingUser, Arg.Any<RoleAssignmentRequest>());
        await userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldFail_WhenTemplateIdsRequiredButMissing(
        string email,
        string name,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisioner provisioner,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var adminEmail = $"admin-{Guid.NewGuid()}@example.com";
        var grantedOn = DateTime.UtcNow;
        var adminUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.AdminRoleId),
            "Admin",
            adminEmail,
            grantedOn,
            null,
            null,
            null);

        // Acting administrator must exist so the handler can resolve GrantedById.
        var userRepoSub = Substitute.For<IEaRepository<User>>();
        var adminUsers = new List<User> { adminUser }.AsQueryable().BuildMockDbSet();
        userRepoSub.Query().Returns(adminUsers);
        SetupHttpContext(httpContextAccessor, adminEmail);

        provisioner.RequiresTemplateIds.Returns(true);
        roleProvisionerRegistry.GetProvisioner(RoleNames.User).Returns(provisioner);

        var handler = CreateHandler(
            userRepoSub,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor);

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, RoleNames.User, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("template ID", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldForbid_WhenChangingPlatformSuperAdminMembership(
        string adminEmail,
        string email,
        string name,
        UserId adminUserId,
        UserId existingUserId,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisioner provisioner,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var templateId = Guid.NewGuid();
        var grantedOn = DateTime.UtcNow;

        var adminUser = new User(
            adminUserId,
            new RoleId(RoleConstants.AdminRoleId),
            "Admin",
            adminEmail,
            grantedOn,
            null,
            null,
            null);

        var existingUser = new User(
            existingUserId,
            new RoleId(RoleConstants.AdminRoleId),
            name,
            email,
            grantedOn,
            null,
            null,
            null);

        var users = new List<User> { adminUser, existingUser }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        SetupHttpContext(httpContextAccessor, adminEmail);

        provisioner.RequiresTemplateIds.Returns(true);
        roleProvisionerRegistry.GetProvisioner(RoleNames.User).Returns(provisioner);

        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor);

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, RoleNames.User, [templateId]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        Assert.Contains("platform SuperAdmin", result.Error, StringComparison.OrdinalIgnoreCase);
        provisioner.DidNotReceive().AssignToExistingUser(Arg.Any<User>(), Arg.Any<RoleAssignmentRequest>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldFail_WhenRoleIsCaseworkerAndNotCreatedAsCustom(
        string email,
        string name,
        IEaRepository<User> userRepo,
        IUnitOfWork unitOfWork,
        IPermissionCheckerService permissionCheckerService,
        IUserRoleProvisionerRegistry roleProvisionerRegistry,
        IHttpContextAccessor httpContextAccessor)
    {
        permissionCheckerService.CanManageUsers().Returns(true);

        var handler = CreateHandler(
            userRepo,
            unitOfWork,
            permissionCheckerService,
            roleProvisionerRegistry,
            httpContextAccessor,
            CreateTenantRoleService(customRole: null));

        var result = await handler.Handle(
            new AssignUserRoleCommand(email, name, RoleNames.Caseworker, [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("was not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetupHttpContext(IHttpContextAccessor httpContextAccessor, string adminEmail)
    {
        var claims = new List<Claim> { new(ClaimTypes.Email, adminEmail) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new DefaultHttpContext { User = principal };
        httpContextAccessor.HttpContext.Returns(context);
    }
}
