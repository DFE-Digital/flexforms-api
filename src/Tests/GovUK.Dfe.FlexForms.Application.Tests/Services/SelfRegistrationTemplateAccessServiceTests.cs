using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using MockQueryable;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class SelfRegistrationTemplateAccessServiceTests
{
    [Fact]
    public async Task EnsureLiveTemplateAccessAsync_GrantsNothing_WhenSeveralFormsAreLiveAndNoDefault()
    {
        var first = new TemplateId(Guid.NewGuid());
        var second = new TemplateId(Guid.NewGuid());
        var userFactory = Substitute.For<IUserFactory>();
        var service = CreateService(
            userFactory,
            liveTemplates: [Live(first, "One"), Live(second, "Two")],
            defaultTemplateId: null);

        var changed = await service.EnsureLiveTemplateAccessAsync(CreateUser());

        Assert.False(changed);
        userFactory.DidNotReceiveWithAnyArgs().EnsureUserHasTemplatePermission(default!, default!, default!, default);
    }

    [Fact]
    public async Task EnsureLiveTemplateAccessAsync_GrantsTheOnlyLiveTemplate()
    {
        var only = new TemplateId(Guid.NewGuid());
        var userFactory = Substitute.For<IUserFactory>();
        var user = CreateUser();
        var service = CreateService(
            userFactory,
            liveTemplates: [Live(only, "Only")],
            defaultTemplateId: null);

        var changed = await service.EnsureLiveTemplateAccessAsync(user);

        Assert.True(changed);
        userFactory.Received(1).EnsureUserHasTemplatePermission(
            user,
            only,
            user.Id!,
            Arg.Any<DateTime>());
    }

    [Fact]
    public async Task EnsureLiveTemplateAccessAsync_GrantsConfiguredDefault_WhenSeveralFormsAreLive()
    {
        var first = new TemplateId(Guid.NewGuid());
        var second = new TemplateId(Guid.NewGuid());
        var userFactory = Substitute.For<IUserFactory>();
        var user = CreateUser();
        var service = CreateService(
            userFactory,
            liveTemplates: [Live(first, "One"), Live(second, "Two")],
            defaultTemplateId: second.Value);

        var changed = await service.EnsureLiveTemplateAccessAsync(user);

        Assert.True(changed);
        userFactory.Received(1).EnsureUserHasTemplatePermission(
            user,
            second,
            user.Id!,
            Arg.Any<DateTime>());
        userFactory.DidNotReceive().EnsureUserHasTemplatePermission(
            user,
            first,
            Arg.Any<UserId>(),
            Arg.Any<DateTime>());
    }

    [Fact]
    public async Task EnsureLiveTemplateAccessAsync_GrantsNothing_WhenNoLiveTemplates()
    {
        var userFactory = Substitute.For<IUserFactory>();
        var service = CreateService(userFactory, liveTemplates: [], defaultTemplateId: Guid.NewGuid());

        var changed = await service.EnsureLiveTemplateAccessAsync(CreateUser());

        Assert.False(changed);
        userFactory.DidNotReceiveWithAnyArgs().EnsureUserHasTemplatePermission(default!, default!, default!, default);
    }

    private static SelfRegistrationTemplateAccessService CreateService(
        IUserFactory userFactory,
        IReadOnlyList<Template> liveTemplates,
        Guid? defaultTemplateId)
    {
        var templateRepo = Substitute.For<IEaRepository<Template>>();
        templateRepo.Query().Returns(liveTemplates.AsQueryable().BuildMock());

        var tenantTemplateResolver = Substitute.For<ITenantTemplateResolver>();
        tenantTemplateResolver.GetTemplateIdsForCurrentTenantAsync(Arg.Any<CancellationToken>())
            .Returns(liveTemplates.Where(t => t.Id is not null).Select(t => t.Id!).ToList());

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(
                defaultTemplateId is null
                    ? new Dictionary<string, string?>()
                    : new Dictionary<string, string?>
                    {
                        [SelfRegistrationTemplateAccessService.DefaultTemplateIdKey] = defaultTemplateId.Value.ToString()
                    })
            .Build();

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(
            Guid.NewGuid(),
            "TestTenant",
            settings,
            []));

        return new SelfRegistrationTemplateAccessService(
            templateRepo,
            tenantTemplateResolver,
            tenantContextAccessor,
            userFactory);
    }

    private static Template Live(TemplateId id, string name) =>
        new(id, name, DateTime.UtcNow, new UserId(Guid.NewGuid()), isLive: true);

    private static User CreateUser() =>
        new(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            "New User",
            "new@example.test",
            DateTime.UtcNow,
            null,
            null,
            null);
}
