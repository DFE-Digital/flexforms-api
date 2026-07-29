using AutoFixture;
using AutoFixture.Xunit2;
using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using MockQueryable;
using MockQueryable.NSubstitute;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Users;

public class GetAllUserPermissionsQueryHandlerTests
{
    private static ITenantMembershipService CreateMembershipService(TenantMembership? membership = null)
    {
        var service = Substitute.For<ITenantMembershipService>();
        service.GetActiveMembershipAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(membership);
        return service;
    }

    private static IRolePermissionService CreateRolePermissionService(
        IReadOnlyList<RolePermission>? permissions = null)
    {
        var service = Substitute.For<IRolePermissionService>();
        service.GetByRoleIdAsync(Arg.Any<RoleId>(), Arg.Any<CancellationToken>())
            .Returns(permissions ?? Array.Empty<RolePermission>());
        return service;
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization), typeof(PermissionCustomization))]
    public async Task Handle_UserWithPermissions_ShouldReturnAuthorizationData(
        UserId userId,
        UserCustomization userCustom,
        PermissionCustomization permCustom,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] ICacheService<IRedisCacheType> cacheService,
        [Frozen] ITenantContextAccessor tenantContextAccessor)
    {
        // Arrange
        userCustom.OverrideId = userId;
        userCustom.OverridePermissions = Array.Empty<Permission>();
        userCustom.OverrideTemplatePermissions = Array.Empty<TemplatePermission>();
        var fixture = new Fixture().Customize(userCustom);
        var user = fixture.Create<User>();

        // Set up the role using reflection
        var role = new Role(user.RoleId, "TestRole");
        user.GetType().GetProperty("Role")!.SetValue(user, role);

        // Add permissions to user
        var permissions = fixture.Customize(permCustom).CreateMany<Permission>().ToList();
        var permissionsBacking = typeof(User).GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        permissionsBacking.SetValue(user, permissions);

        var templatePermissions = fixture.CreateMany<TemplatePermission>().ToList();
        var templatePermissionsBacking = typeof(User).GetField("_templatePermissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        templatePermissionsBacking.SetValue(user, templatePermissions);

        var userQueryable = new List<User> { user }.AsQueryable().BuildMock();
        userRepo.Query().Returns(userQueryable);

        cacheService.GetOrAddAsync(
            Arg.Any<string>(),
            Arg.Any<Func<Task<Result<UserAuthorizationDto>>>>(),
            nameof(GetAllUserPermissionsQueryHandler))
            .Returns(call =>
            {
                var func = call.Arg<Func<Task<Result<UserAuthorizationDto>>>>();
                return func();
            });

        var handler = new GetAllUserPermissionsQueryHandler(
            userRepo,
            cacheService,
            tenantContextAccessor,
            CreateMembershipService(),
            CreateRolePermissionService());

        // Act
        var result = await handler.Handle(new GetAllUserPermissionsQuery(userId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.Permissions);
        Assert.NotEmpty(result.Value.Roles);
        Assert.Single(result.Value.Roles);
        Assert.Equal("TestRole", result.Value.Roles.First());
        Assert.All(permissions, permission =>
        {
            var dto = result.Value.Permissions.First(p =>
                p.ResourceType == permission.ResourceType
                && p.ResourceKey == permission.ResourceKey
                && p.AccessType == permission.AccessType);
            Assert.Equal(permission.ApplicationId?.Value, dto.ApplicationId);
        });
        Assert.All(templatePermissions, templatePermission =>
        {
            var dto = result.Value.Permissions.First(p =>
                p.ResourceType == ResourceType.Template
                && p.ResourceKey == templatePermission.TemplateId.Value.ToString()
                && p.AccessType == templatePermission.AccessType);
            Assert.NotNull(dto);
        });
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_UserNotFound_ShouldReturnEmptyData(
        UserId userId,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] ICacheService<IRedisCacheType> cacheService,
        [Frozen] ITenantContextAccessor tenantContextAccessor)
    {
        // Arrange
        var emptyQueryable = new List<User>().AsQueryable().BuildMock();
        userRepo.Query().Returns(emptyQueryable);

        cacheService.GetOrAddAsync(
            Arg.Any<string>(),
            Arg.Any<Func<Task<Result<UserAuthorizationDto>>>>(),
            nameof(GetAllUserPermissionsQueryHandler))
            .Returns(call =>
            {
                var func = call.Arg<Func<Task<Result<UserAuthorizationDto>>>>();
                return func();
            });

        var handler = new GetAllUserPermissionsQueryHandler(
            userRepo,
            cacheService,
            tenantContextAccessor,
            CreateMembershipService(),
            CreateRolePermissionService());

        // Act
        var result = await handler.Handle(new GetAllUserPermissionsQuery(userId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.Permissions);
        Assert.Empty(result.Value.Roles);
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_Exception_ShouldReturnFailure(
        UserId userId,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] ICacheService<IRedisCacheType> cacheService,
        [Frozen] ITenantContextAccessor tenantContextAccessor)
    {
        // Arrange
        userRepo.Query().Throws(new Exception("Test exception"));

        cacheService.GetOrAddAsync(
            Arg.Any<string>(),
            Arg.Any<Func<Task<Result<UserAuthorizationDto>>>>(),
            nameof(GetAllUserPermissionsQueryHandler))
            .Returns(call =>
            {
                var func = call.Arg<Func<Task<Result<UserAuthorizationDto>>>>();
                return func();
            });

        var handler = new GetAllUserPermissionsQueryHandler(
            userRepo,
            cacheService,
            tenantContextAccessor,
            CreateMembershipService(),
            CreateRolePermissionService());

        // Act
        var result = await handler.Handle(new GetAllUserPermissionsQuery(userId), CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Test exception", result.Error);
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_CacheHit_ShouldReturnCachedResult(
        UserId userId,
        UserAuthorizationDto cachedAuthorization,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] ICacheService<IRedisCacheType> cacheService,
        [Frozen] ITenantContextAccessor tenantContextAccessor)
    {
        // Arrange
        cacheService.GetOrAddAsync(
            Arg.Any<string>(),
            Arg.Any<Func<Task<Result<UserAuthorizationDto>>>>(),
            nameof(GetAllUserPermissionsQueryHandler))
            .Returns(Result<UserAuthorizationDto>.Success(cachedAuthorization));

        var handler = new GetAllUserPermissionsQueryHandler(
            userRepo,
            cacheService,
            tenantContextAccessor,
            CreateMembershipService(),
            CreateRolePermissionService());

        // Act
        var result = await handler.Handle(new GetAllUserPermissionsQuery(userId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(cachedAuthorization, result.Value);
        userRepo.DidNotReceive().Query();
    }
} 
