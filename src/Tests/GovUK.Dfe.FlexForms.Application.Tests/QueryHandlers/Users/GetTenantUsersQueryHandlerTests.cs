using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using MockQueryable;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Users;

public class GetTenantUsersQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerCannotManageUsers()
    {
        var permissionChecker = Substitute.For<IPermissionCheckerService>();
        permissionChecker.CanManageUsers().Returns(false);

        var handler = CreateHandler(permissionChecker: permissionChecker);

        var result = await handler.Handle(new GetTenantUsersQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenTenantContextIsMissing()
    {
        var permissionChecker = Substitute.For<IPermissionCheckerService>();
        permissionChecker.CanManageUsers().Returns(true);
        var tenantContext = Substitute.For<ITenantContextAccessor>();
        tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var handler = CreateHandler(permissionChecker: permissionChecker, tenantContext: tenantContext);

        var result = await handler.Handle(new GetTenantUsersQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyPage_WhenThereAreNoMemberships()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(new GetTenantUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(0, result.Value.TotalPages);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(GetTenantUsersQuery.DefaultPageSize, result.Value.PageSize);
    }

    [Fact]
    public async Task Handle_ShouldPageMembershipsInSqlOrder_AndLoadTemplateAccessForThePageOnly()
    {
        var role = Role.CreateForTenant(TenantId, RoleNames.User, true);
        var users = Enumerable.Range(1, 12)
            .Select(i => CreateUser($"User {i:00}", $"user{i:00}@example.test", role.Id!))
            .ToList();
        var memberships = users.Select(u => CreateMembership(TenantId, u, role)).ToList();

        var pageUser = users[0];
        var offPageUser = users[11];
        var catalogueTemplateId = new TemplateId(Guid.NewGuid());
        var otherTemplateId = new TemplateId(Guid.NewGuid());
        var template = new Template(catalogueTemplateId, "Alpha form", DateTime.UtcNow, pageUser.Id!);

        var membershipRepository = Substitute.For<IEaRepository<TenantMembership>>();
        membershipRepository.Query().Returns(memberships.AsQueryable().BuildMock());

        var permissionRepository = Substitute.For<IEaRepository<Permission>>();
        permissionRepository.Query().Returns(new[]
        {
            CreateTemplatePermission(pageUser.Id!, catalogueTemplateId),
            CreateTemplatePermission(offPageUser.Id!, catalogueTemplateId),
            CreateTemplatePermission(pageUser.Id!, otherTemplateId)
        }.AsQueryable().BuildMock());

        var templateRepository = Substitute.For<IEaRepository<Template>>();
        templateRepository.Query().Returns(new[] { template }.AsQueryable().BuildMock());

        var catalogue = Substitute.For<ITenantTemplateCatalogue>();
        catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>()).Returns([catalogueTemplateId]);

        var handler = CreateHandler(
            membershipRepository: membershipRepository,
            permissionRepository: permissionRepository,
            templateRepository: templateRepository,
            catalogue: catalogue);

        var page1 = await handler.Handle(new GetTenantUsersQuery(PageNumber: 1, PageSize: 10), CancellationToken.None);

        Assert.True(page1.IsSuccess);
        Assert.Equal(12, page1.Value!.TotalCount);
        Assert.Equal(2, page1.Value.TotalPages);
        Assert.Equal(10, page1.Value.Items.Count);
        Assert.Equal("User 01", page1.Value.Items.First().Name);
        Assert.Equal("User 10", page1.Value.Items.Last().Name);

        var first = page1.Value.Items.First();
        var granted = Assert.Single(first.Templates);
        Assert.Equal(catalogueTemplateId.Value, granted.TemplateId);
        Assert.Equal("Alpha form", granted.TemplateName);

        var page2 = await handler.Handle(new GetTenantUsersQuery(PageNumber: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(2, page2.Value!.Items.Count);
        Assert.Equal("User 11", page2.Value.Items.First().Name);
        Assert.Equal(2, page2.Value.PageNumber);
    }

    [Fact]
    public async Task Handle_ShouldClampPageNumber_WhenRequestedPageIsPastTheEnd()
    {
        var role = Role.CreateForTenant(TenantId, RoleNames.User, true);
        var user = CreateUser("Ada", "ada@example.test", role.Id!);
        var membershipRepository = Substitute.For<IEaRepository<TenantMembership>>();
        membershipRepository.Query().Returns(new[] { CreateMembership(TenantId, user, role) }.AsQueryable().BuildMock());

        var handler = CreateHandler(membershipRepository: membershipRepository);

        var result = await handler.Handle(new GetTenantUsersQuery(PageNumber: 9, PageSize: 10), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PageNumber);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(user.Id!.Value, Assert.Single(result.Value.Items).UserId);
    }

    [Fact]
    public async Task Handle_ShouldFilterByUserId()
    {
        var role = Role.CreateForTenant(TenantId, RoleNames.User, true);
        var keep = CreateUser("Keep", "keep@example.test", role.Id!);
        var drop = CreateUser("Drop", "drop@example.test", role.Id!);
        var membershipRepository = Substitute.For<IEaRepository<TenantMembership>>();
        membershipRepository.Query().Returns(new[]
        {
            CreateMembership(TenantId, keep, role),
            CreateMembership(TenantId, drop, role)
        }.AsQueryable().BuildMock());

        var handler = CreateHandler(membershipRepository: membershipRepository);

        var result = await handler.Handle(
            new GetTenantUsersQuery(PageNumber: 1, PageSize: 10, UserId: keep.Id!.Value),
            CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(keep.Id.Value, item.UserId);
        Assert.Equal(1, result.Value.TotalCount);
    }

    private static GetTenantUsersQueryHandler CreateHandler(
        IPermissionCheckerService? permissionChecker = null,
        ITenantContextAccessor? tenantContext = null,
        IEaRepository<TenantMembership>? membershipRepository = null,
        IEaRepository<Permission>? permissionRepository = null,
        IEaRepository<Template>? templateRepository = null,
        ITenantTemplateCatalogue? catalogue = null)
    {
        if (permissionChecker is null)
        {
            permissionChecker = Substitute.For<IPermissionCheckerService>();
            permissionChecker.CanManageUsers().Returns(true);
        }

        if (tenantContext is null)
        {
            tenantContext = Substitute.For<ITenantContextAccessor>();
            tenantContext.CurrentTenant.Returns(new TenantConfiguration(
                TenantId,
                "TestTenant",
                new ConfigurationBuilder().Build(),
                []));
        }

        if (membershipRepository is null)
        {
            membershipRepository = Substitute.For<IEaRepository<TenantMembership>>();
            membershipRepository.Query().Returns(Array.Empty<TenantMembership>().AsQueryable().BuildMock());
        }

        if (permissionRepository is null)
        {
            permissionRepository = Substitute.For<IEaRepository<Permission>>();
            permissionRepository.Query().Returns(Array.Empty<Permission>().AsQueryable().BuildMock());
        }

        if (templateRepository is null)
        {
            templateRepository = Substitute.For<IEaRepository<Template>>();
            templateRepository.Query().Returns(Array.Empty<Template>().AsQueryable().BuildMock());
        }

        if (catalogue is null)
        {
            catalogue = Substitute.For<ITenantTemplateCatalogue>();
            catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        }

        return new GetTenantUsersQueryHandler(
            membershipRepository,
            permissionRepository,
            templateRepository,
            catalogue,
            tenantContext,
            permissionChecker);
    }

    private static User CreateUser(string name, string email, RoleId roleId)
    {
        return new User(
            new UserId(Guid.NewGuid()),
            roleId,
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null);
    }

    private static TenantMembership CreateMembership(Guid tenantId, User user, Role role)
    {
        var membership = TenantMembership.Create(tenantId, user.Id!, role.Id!, DateTime.UtcNow);
        membership.GetType().GetProperty(nameof(TenantMembership.User))!.SetValue(membership, user);
        membership.GetType().GetProperty(nameof(TenantMembership.Role))!.SetValue(membership, role);
        return membership;
    }

    private static Permission CreateTemplatePermission(UserId userId, TemplateId templateId)
    {
        return new Permission(
            new PermissionId(Guid.NewGuid()),
            userId,
            applicationId: null,
            templateId.Value.ToString(),
            ResourceType.Template,
            AccessType.Read,
            DateTime.UtcNow,
            userId);
    }
}
