using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MockQueryable.NSubstitute;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantMembershipServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly UserId Bob = new(Guid.Parse("b0000000-0000-4000-8000-000000000001"));

    private readonly IEaRepository<TenantMembership> _memberships = Substitute.For<IEaRepository<TenantMembership>>();
    private readonly ITenantRoleService _roles = Substitute.For<ITenantRoleService>();
    private readonly List<TenantMembership> _stored = [];

    public TenantMembershipServiceTests()
    {
        _roles.GetOrCreateTenantRoleAsync(default, default!, default)
            .ReturnsForAnyArgs(ci => new Role(new RoleId(Guid.NewGuid()), ci.ArgAt<string>(1), ci.ArgAt<Guid>(0)));
    }

    [Fact]
    public async Task GetActive_ReturnsOnlyAnActiveMembershipOfThatTenant()
    {
        var active = Store(TenantId, Bob, isActive: true);
        Store(OtherTenantId, Bob, isActive: false);

        Assert.Same(active, await Service().GetActiveMembershipAsync(TenantId, Bob));
        Assert.Null(await Service().GetActiveMembershipAsync(OtherTenantId, Bob));
        Assert.Null(await Service().GetActiveMembershipAsync(TenantId, new UserId(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData("user")]
    [InlineData(RoleNames.User)]
    public async Task Upsert_NewMember_IsAddedActiveWithTheRole(string roleName)
    {
        var membership = await Service().UpsertMembershipAsync(TenantId, Bob, roleName);

        Assert.Equal(TenantId, membership.TenantId);
        Assert.Equal(Bob, membership.UserId);
        Assert.True(membership.IsActive);
        await _roles.Received(1).EnsureSystemRolesAsync(TenantId, Arg.Any<CancellationToken>());
        await _roles.Received(1).GetOrCreateTenantRoleAsync(TenantId, roleName, Arg.Any<CancellationToken>());
        await _memberships.Received(1).AddAsync(membership, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upsert_ExistingInactiveMember_IsReassignedAndReactivated()
    {
        var existing = Store(TenantId, Bob, isActive: false);
        var adminRole = new Role(new RoleId(Guid.NewGuid()), RoleNames.Admin, TenantId);
        _roles.GetOrCreateTenantRoleAsync(TenantId, RoleNames.Admin, Arg.Any<CancellationToken>()).Returns(adminRole);

        var membership = await Service().UpsertMembershipAsync(TenantId, Bob, RoleNames.Admin);

        Assert.Same(existing, membership);
        Assert.True(membership.IsActive);
        Assert.Equal(adminRole.Id, membership.RoleId);
        Assert.NotNull(membership.LastModifiedOn);
        await _memberships.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Upsert_MembershipOfAnotherTenant_IsNotReused()
    {
        var other = Store(OtherTenantId, Bob, isActive: true);

        var membership = await Service().UpsertMembershipAsync(TenantId, Bob, RoleNames.User);

        Assert.NotSame(other, membership);
        Assert.Equal(TenantId, membership.TenantId);
        await _memberships.Received(1).AddAsync(membership, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deactivate_ExistingMember_IsDeactivated()
    {
        var existing = Store(TenantId, Bob, isActive: true);
        var other = Store(OtherTenantId, Bob, isActive: true);

        await Service().DeactivateMembershipAsync(TenantId, Bob);

        Assert.False(existing.IsActive);
        Assert.True(other.IsActive);
    }

    [Fact]
    public async Task Deactivate_NonMember_DoesNothing()
    {
        await Service().DeactivateMembershipAsync(TenantId, Bob);

        await _memberships.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    private TenantMembership Store(Guid tenantId, UserId userId, bool isActive)
    {
        var membership = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()), tenantId, userId, new RoleId(Guid.NewGuid()), DateTime.UtcNow, isActive);
        _stored.Add(membership);
        return membership;
    }

    private TenantMembershipService Service()
    {
        var set = _stored.BuildMockDbSet();
        _memberships.Query().Returns(set);
        return new TenantMembershipService(_memberships, _roles);
    }
}
