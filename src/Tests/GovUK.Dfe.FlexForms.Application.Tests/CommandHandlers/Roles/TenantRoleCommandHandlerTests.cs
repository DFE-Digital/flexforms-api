using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.FlexForms.Application.Roles.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Roles;

public class TenantRoleCommandHandlerTests
{
    private static (ITenantContextAccessor Accessor, Guid TenantId) CreateTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var tenant = new TenantConfiguration(tenantId, "Test", config, Array.Empty<string>());
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);
        return (accessor, tenantId);
    }

    [Fact]
    public async Task Create_ShouldForbid_WhenNotAdmin()
    {
        var permissionChecker = Substitute.For<IPermissionCheckerService>();
        permissionChecker.IsAdmin().Returns(false);
        var (accessor, _) = CreateTenantContext();

        var handler = new CreateTenantRoleCommandHandler(
            permissionChecker,
            accessor,
            Substitute.For<ITenantRoleService>(),
            Substitute.For<IUnitOfWork>());

        var result = await handler.Handle(new CreateTenantRoleCommand("Reviewer"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Create_ShouldSucceed_WhenAdmin()
    {
        var permissionChecker = Substitute.For<IPermissionCheckerService>();
        permissionChecker.IsAdmin().Returns(true);
        var (accessor, tenantId) = CreateTenantContext();
        var roleService = Substitute.For<ITenantRoleService>();
        var created = Role.CreateForTenant(tenantId, "Reviewer", isSystem: false);
        roleService.CreateCustomRoleAsync(tenantId, "Reviewer", Arg.Any<CancellationToken>())
            .Returns(created);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var handler = new CreateTenantRoleCommandHandler(
            permissionChecker,
            accessor,
            roleService,
            unitOfWork);

        var result = await handler.Handle(new CreateTenantRoleCommand("Reviewer"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Reviewer", result.Value!.Name);
        Assert.False(result.Value.IsSystem);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPermissions_ShouldRejectSystemRole()
    {
        var permissionChecker = Substitute.For<IPermissionCheckerService>();
        permissionChecker.IsAdmin().Returns(true);
        var (accessor, tenantId) = CreateTenantContext();
        var systemRole = Role.CreateForTenant(tenantId, "User", isSystem: true);
        var roleService = Substitute.For<ITenantRoleService>();
        roleService.GetByIdAsync(tenantId, Arg.Any<RoleId>(), Arg.Any<CancellationToken>())
            .Returns(systemRole);

        var rolePermissionService = Substitute.For<IRolePermissionService>();
        rolePermissionService
            .When(s => s.ReplacePermissionsAsync(
                Arg.Any<Role>(),
                Arg.Any<IReadOnlyCollection<(ResourceType, string, AccessType)>>(),
                Arg.Any<CancellationToken>()))
            .Do(ci =>
            {
                var role = ci.ArgAt<Role>(0);
                var grants = ci.ArgAt<IReadOnlyCollection<(ResourceType, string, AccessType)>>(1);
                role.BuildReplacedPermissions(grants, DateTime.UtcNow);
            });

        var handler = new SetRolePermissionsCommandHandler(
            permissionChecker,
            accessor,
            roleService,
            rolePermissionService,
            Substitute.For<IApplicationRepository>(),
            Substitute.For<ITenantTemplateCatalogue>(),
            Substitute.For<IEaRepository<User>>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IUserCacheInvalidator>());

        var result = await handler.Handle(
            new SetRolePermissionsCommand(
                systemRole.Id!.Value,
                [new RolePermissionGrantDto
                {
                    ResourceType = ResourceType.Template,
                    ResourceKey = "Any",
                    AccessType = AccessType.Write
                }]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("System role", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateValidator_RejectsEmptyName()
    {
        var validator = new CreateTenantRoleCommandValidator();
        var result = validator.Validate(new CreateTenantRoleCommand(""));
        Assert.False(result.IsValid);
    }
}
