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

    [Fact]
    public async Task EnsureLiveTemplateAccessAsync_GrantsEveryLiveConfiguredDefault_FromArray()
    {
        var first = new TemplateId(Guid.NewGuid());
        var second = new TemplateId(Guid.NewGuid());
        var third = new TemplateId(Guid.NewGuid());
        var userFactory = Substitute.For<IUserFactory>();
        var user = CreateUser();
        var service = CreateService(
            userFactory,
            liveTemplates: [Live(first, "One"), Live(second, "Two"), Live(third, "Three")],
            settings: new Dictionary<string, string?>
            {
                [$"{SelfRegistrationTemplateAccessService.DefaultTemplateIdsKey}:0"] = first.Value.ToString(),
                [$"{SelfRegistrationTemplateAccessService.DefaultTemplateIdsKey}:1"] = Guid.NewGuid().ToString(),
                [$"{SelfRegistrationTemplateAccessService.DefaultTemplateIdsKey}:2"] = "not-a-guid",
                [SelfRegistrationTemplateAccessService.DefaultTemplateIdKey] = third.Value.ToString()
            });

        var changed = await service.EnsureLiveTemplateAccessAsync(user);

        Assert.True(changed);
        userFactory.Received(1).EnsureUserHasTemplatePermission(user, first, user.Id!, Arg.Any<DateTime>());
        userFactory.Received(1).EnsureUserHasTemplatePermission(user, third, user.Id!, Arg.Any<DateTime>());
        userFactory.DidNotReceive().EnsureUserHasTemplatePermission(user, second, Arg.Any<UserId>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task EnsureLiveTemplateAccessAsync_GrantsConfiguredDefaults_FromDelimitedString()
    {
        var first = new TemplateId(Guid.NewGuid());
        var second = new TemplateId(Guid.NewGuid());
        var third = new TemplateId(Guid.NewGuid());
        var userFactory = Substitute.For<IUserFactory>();
        var user = CreateUser();
        var service = CreateService(
            userFactory,
            liveTemplates: [Live(first, "One"), Live(second, "Two"), Live(third, "Three")],
            settings: new Dictionary<string, string?>
            {
                [SelfRegistrationTemplateAccessService.DefaultTemplateIdsKey] = $" {first.Value} ; {second.Value}"
            });

        var changed = await service.EnsureLiveTemplateAccessAsync(user);

        Assert.True(changed);
        userFactory.Received(1).EnsureUserHasTemplatePermission(user, first, user.Id!, Arg.Any<DateTime>());
        userFactory.Received(1).EnsureUserHasTemplatePermission(user, second, user.Id!, Arg.Any<DateTime>());
        userFactory.DidNotReceive().EnsureUserHasTemplatePermission(user, third, Arg.Any<UserId>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task EnsureLiveTemplateAccessAsync_IgnoresLegacyDefault_WhenSelfRegistrationDefaultsAreSet()
    {
        var first = new TemplateId(Guid.NewGuid());
        var legacy = new TemplateId(Guid.NewGuid());
        var userFactory = Substitute.For<IUserFactory>();
        var user = CreateUser();
        var service = CreateService(
            userFactory,
            liveTemplates: [Live(first, "One"), Live(legacy, "Legacy")],
            settings: new Dictionary<string, string?>
            {
                [$"{SelfRegistrationTemplateAccessService.DefaultTemplateIdsKey}:0"] = first.Value.ToString(),
                [SelfRegistrationTemplateAccessService.LegacyDefaultTemplateIdKey] = legacy.Value.ToString()
            });

        await service.EnsureLiveTemplateAccessAsync(user);

        userFactory.Received(1).EnsureUserHasTemplatePermission(user, first, user.Id!, Arg.Any<DateTime>());
        userFactory.DidNotReceive().EnsureUserHasTemplatePermission(user, legacy, Arg.Any<UserId>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task EnsureLiveTemplateAccessAsync_UsesLegacyDefault_WhenNoSelfRegistrationDefaults()
    {
        var first = new TemplateId(Guid.NewGuid());
        var legacy = new TemplateId(Guid.NewGuid());
        var userFactory = Substitute.For<IUserFactory>();
        var user = CreateUser();
        var service = CreateService(
            userFactory,
            liveTemplates: [Live(first, "One"), Live(legacy, "Legacy")],
            settings: new Dictionary<string, string?>
            {
                [SelfRegistrationTemplateAccessService.LegacyDefaultTemplateIdKey] = legacy.Value.ToString()
            });

        var changed = await service.EnsureLiveTemplateAccessAsync(user);

        Assert.True(changed);
        userFactory.Received(1).EnsureUserHasTemplatePermission(user, legacy, user.Id!, Arg.Any<DateTime>());
        userFactory.DidNotReceive().EnsureUserHasTemplatePermission(user, first, Arg.Any<UserId>(), Arg.Any<DateTime>());
    }

    private static SelfRegistrationTemplateAccessService CreateService(
        IUserFactory userFactory,
        IReadOnlyList<Template> liveTemplates,
        Guid? defaultTemplateId) =>
        CreateService(
            userFactory,
            liveTemplates,
            defaultTemplateId is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>
                {
                    [SelfRegistrationTemplateAccessService.DefaultTemplateIdKey] = defaultTemplateId.Value.ToString()
                });

    private static SelfRegistrationTemplateAccessService CreateService(
        IUserFactory userFactory,
        IReadOnlyList<Template> liveTemplates,
        IDictionary<string, string?> settings)
    {
        var templateRepo = Substitute.For<IEaRepository<Template>>();
        templateRepo.Query().Returns(liveTemplates.AsQueryable().BuildMock());

        var tenantTemplateResolver = Substitute.For<ITenantTemplateResolver>();
        tenantTemplateResolver.GetTemplateIdsForCurrentTenantAsync(Arg.Any<CancellationToken>())
            .Returns(liveTemplates.Where(t => t.Id is not null).Select(t => t.Id!).ToList());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(
            Guid.NewGuid(),
            "TestTenant",
            configuration,
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
