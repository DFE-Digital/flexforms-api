using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

/// <summary>
/// What the HTTP tests (UsersControllerPermissionsTests) can't see: the handler's own guards, which the
/// controller's CanManageUsers policy reaches first, and the cache clear, which the test host doesn't cache.
/// </summary>
public sealed class SetUserPermissionsCommandHandlerTests
{
    private readonly User _admin = new(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "Admin", "admin@example.com", DateTime.UtcNow, null, null, null);
    private readonly User _subject = new(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "Subject", "subject@example.com", DateTime.UtcNow, null, null, null, externalProviderId: "subject-ext");

    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly ITenantMembershipService _memberships = Substitute.For<ITenantMembershipService>();
    private readonly IEaRepository<User> _users = Substitute.For<IEaRepository<User>>();
    private readonly ITenantPermissionFilter _filter = Substitute.For<ITenantPermissionFilter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IUserCacheInvalidator _cache = Substitute.For<IUserCacheInvalidator>();
    private readonly IHttpContextAccessor _http = Substitute.For<IHttpContextAccessor>();

    public SetUserPermissionsCommandHandlerTests()
    {
        var tenantId = Guid.NewGuid();
        _permissions.CanManageUsers().Returns(true);
        _tenant.CurrentTenant.Returns(new TenantConfiguration(tenantId, "Tenant", new ConfigurationBuilder().Build(), []));
        _memberships.GetActiveMembershipAsync(tenantId, _subject.Id!, Arg.Any<CancellationToken>())
            .Returns(new TenantMembership(new TenantMembershipId(Guid.NewGuid()), tenantId, _subject.Id!, _subject.RoleId, DateTime.UtcNow));
        _filter.FilterToCurrentTenantAsync(Arg.Any<IEnumerable<Permission>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<Permission>>().ToList());
        var users = new[] { _admin, _subject }.AsQueryable().BuildMockDbSet();
        _users.Query().Returns(users);
        _http.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, _admin.Email)], "Bearer"))
        });
    }

    [Fact]
    public async Task Saving_ClearsTheUsersCachedPermissions()
    {
        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _cache.Received(1).InvalidateForUserAsync(_subject.Email, _subject.ExternalProviderId, _subject.Id!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsRefused_WithoutUserManagement_OrATenant()
    {
        _permissions.CanManageUsers().Returns(false);
        var noPermission = await Handler().Handle(Command(), CancellationToken.None);

        _permissions.CanManageUsers().Returns(true);
        _tenant.CurrentTenant.Returns((TenantConfiguration?)null);
        var noTenant = await Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, noPermission.ErrorCode);
        Assert.Equal(DomainErrorCode.Forbidden, noTenant.ErrorCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task Fails_WhenTheActingAdministratorCannotBeFound()
    {
        _http.HttpContext.Returns(new DefaultHttpContext());

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Could not resolve the acting administrator", result.Error);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task RejectsAManageGrant_EvenIfTheValidatorIsBypassed()
    {
        var result = await Handler().Handle(
            new SetUserPermissionsCommand(_subject.Id!.Value, [Grant(ResourceType.User, PermissionConstants.AnyResourceKey, AccessType.Manage)]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("'Manage' cannot be granted to an individual user", result.Error);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    private SetUserPermissionsCommand Command() =>
        new(_subject.Id!.Value, [Grant(ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read)]);

    private static RolePermissionGrantDto Grant(ResourceType type, string key, AccessType access) =>
        new() { ResourceType = type, ResourceKey = key, AccessType = access };

    private SetUserPermissionsCommandHandler Handler() => new(
        _permissions,
        _tenant,
        _memberships,
        _users,
        Substitute.For<IApplicationRepository>(),
        Substitute.For<ITenantTemplateCatalogue>(),
        _filter,
        new UserFactory(),
        _unitOfWork,
        _cache,
        _http);
}
