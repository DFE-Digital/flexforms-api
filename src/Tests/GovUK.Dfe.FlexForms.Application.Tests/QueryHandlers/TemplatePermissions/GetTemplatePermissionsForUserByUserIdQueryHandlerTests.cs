using AutoFixture;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.TemplatePermissions.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using MockQueryable;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.TemplatePermissions;

public class GetTemplatePermissionsForUserByUserIdQueryHandlerTests
{
    private readonly IEaRepository<User> _userRepo;
    private readonly ICacheService<IRedisCacheType> _cacheService;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly GetTemplatePermissionsForUserByUserIdQueryHandler _handler;

    public GetTemplatePermissionsForUserByUserIdQueryHandlerTests()
    {
        _userRepo = Substitute.For<IEaRepository<User>>();
        _cacheService = Substitute.For<ICacheService<IRedisCacheType>>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        // Configure cache service to execute the factory directly (bypass caching)
        _cacheService.GetOrAddAsync(
                Arg.Any<string>(),
                Arg.Any<Func<Task<Result<IReadOnlyCollection<TemplatePermissionDto>>>>>(),
                Arg.Any<string>())
            .Returns(callInfo =>
            {
                var factory = callInfo.ArgAt<Func<Task<Result<IReadOnlyCollection<TemplatePermissionDto>>>>>(1);
                return factory();
            });

        _handler = new GetTemplatePermissionsForUserByUserIdQueryHandler(
            _userRepo, _cacheService, _tenantContextAccessor);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization), typeof(PermissionCustomization))]
    public async Task Handle_ShouldReturnTemplatePermissions_WhenUserExists(
        UserCustomization userCustom,
        PermissionCustomization permCustom)
    {
        // Arrange
        var userId = new UserId(Guid.NewGuid());
        var templateId = Guid.NewGuid();
        userCustom.OverrideId = userId;
        var fixture = new Fixture().Customize(userCustom);
        var user = fixture.Create<User>();

        var backingField = typeof(User)
            .GetField("_permissions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        backingField.SetValue(user, new List<Permission>());

        permCustom.OverrideUserId = userId;
        permCustom.OverrideAppId = null;
        permCustom.OverrideResourceType = ResourceType.Template;
        permCustom.OverrideResourceKey = templateId.ToString();
        permCustom.OverrideAccessType = AccessType.Read;
        var templatePermission = new Fixture().Customize(permCustom).Create<Permission>();
        ((List<Permission>)backingField.GetValue(user)!).Add(templatePermission);

        var userList = new List<User> { user };
        _userRepo.Query().Returns(userList.AsQueryable().BuildMock());

        // Act
        var result = await _handler.Handle(
            new GetTemplatePermissionsForUserByUserIdQuery(userId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(templatePermission.Id!.Value, result.Value!.First().TemplatePermissionId);
        Assert.Equal(templateId, result.Value!.First().TemplateId);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldReturnEmpty_WhenUserNotFound(
        UserCustomization userCustom)
    {
        // Arrange
        var userId = new UserId(Guid.NewGuid());
        userCustom.OverrideId = new UserId(Guid.NewGuid());
        var user = new Fixture().Customize(userCustom).Create<User>();
        var userQ = new List<User> { user }.AsQueryable().BuildMock();
        _userRepo.Query().Returns(userQ);

        // Act
        var result = await _handler.Handle(
            new GetTemplatePermissionsForUserByUserIdQuery(userId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenExceptionOccurs()
    {
        // Arrange
        var userId = new UserId(Guid.NewGuid());
        _userRepo.Query().Throws(new Exception("Boom"));

        // Reset the cache service to also propagate the exception
        _cacheService.GetOrAddAsync(
                Arg.Any<string>(),
                Arg.Any<Func<Task<Result<IReadOnlyCollection<TemplatePermissionDto>>>>>(),
                Arg.Any<string>())
            .ThrowsAsync(new Exception("Boom"));

        // Act
        var result = await _handler.Handle(
            new GetTemplatePermissionsForUserByUserIdQuery(userId), CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Boom", result.Error);
    }
}
