using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class DatabaseTenantConfigurationProviderTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly ServiceProvider _services;
    private readonly ITenantConfigurationChangedNotifier _notifier = Substitute.For<ITenantConfigurationChangedNotifier>();

    public DatabaseTenantConfigurationProviderTests()
    {
        _services = new ServiceCollection()
            .AddDbContext<TenantConfigDbContext>(o => o.UseInMemoryDatabase(_databaseName))
            .BuildServiceProvider();
    }

    private DatabaseTenantConfigurationProvider CreateProvider()
        => new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<DatabaseTenantConfigurationProvider>>(),
            changeNotifier: _notifier);

    private async Task WithDbAsync(Func<TenantConfigDbContext, Task> action)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantConfigDbContext>();
        await action(db);
        await db.SaveChangesAsync();
    }

    private static TenantEntity Tenant(Guid id, string name, string dfeSignInJson, params string[] origins)
        => new()
        {
            Id = id,
            Name = name,
            Settings =
            {
                new TenantSettingEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = id,
                    Category = "DfESignIn",
                    Target = "Api",
                    Settings = dfeSignInJson
                }
            },
            FrontendOrigins = origins
                .Select(o => new TenantFrontendOriginEntity { Id = Guid.NewGuid(), TenantId = id, Origin = o })
                .ToList()
        };

    [Fact]
    public async Task RefreshAsync_FirstLoad_SwapsSnapshotAndNotifies()
    {
        await WithDbAsync(db =>
        {
            db.Tenants.Add(Tenant(TenantA, "A", """{"ClientId":"client-a"}""", "https://a.example.com"));
            return Task.CompletedTask;
        });

        var provider = CreateProvider();

        await provider.RefreshAsync();

        _notifier.Received(1).Notify();
        Assert.Equal("client-a", provider.GetTenant(TenantA)!.Settings["DfESignIn:ClientId"]);
        Assert.Same(provider.GetTenant(TenantA), provider.GetTenantByOrigin("https://a.example.com"));
    }

    [Fact]
    public async Task RefreshAsync_Unchanged_DoesNotNotifyOrReplaceSnapshot()
    {
        await WithDbAsync(db =>
        {
            db.Tenants.Add(Tenant(TenantA, "A", """{"ClientId":"client-a"}""", "https://a.example.com"));
            db.Tenants.Add(Tenant(TenantB, "B", """{"ClientId":"client-b"}"""));
            return Task.CompletedTask;
        });

        var provider = CreateProvider();
        await provider.RefreshAsync();
        var firstSnapshot = provider.GetTenant(TenantA);
        var firstRefreshedUtc = provider.LastRefreshedUtc;
        _notifier.ClearReceivedCalls();

        await provider.RefreshAsync();
        await provider.RefreshAsync();

        _notifier.DidNotReceive().Notify();
        Assert.Same(firstSnapshot, provider.GetTenant(TenantA));
        Assert.True(provider.LastRefreshedUtc >= firstRefreshedUtc);
    }

    [Fact]
    public async Task RefreshAsync_SettingValueChanged_NotifiesWithNewValue()
    {
        await WithDbAsync(db =>
        {
            db.Tenants.Add(Tenant(TenantA, "A", """{"ClientId":"client-a"}"""));
            return Task.CompletedTask;
        });

        var provider = CreateProvider();
        await provider.RefreshAsync();
        _notifier.ClearReceivedCalls();

        await WithDbAsync(async db =>
        {
            var setting = await db.TenantSettings.SingleAsync(s => s.TenantId == TenantA);
            setting.Settings = """{"ClientId":"client-a-rotated"}""";
        });

        await provider.RefreshAsync();

        _notifier.Received(1).Notify();
        Assert.Equal("client-a-rotated", provider.GetTenant(TenantA)!.Settings["DfESignIn:ClientId"]);
    }

    [Fact]
    public async Task RefreshAsync_TenantDeactivated_Notifies()
    {
        await WithDbAsync(db =>
        {
            db.Tenants.Add(Tenant(TenantA, "A", """{"ClientId":"client-a"}"""));
            db.Tenants.Add(Tenant(TenantB, "B", """{"ClientId":"client-b"}"""));
            return Task.CompletedTask;
        });

        var provider = CreateProvider();
        await provider.RefreshAsync();
        _notifier.ClearReceivedCalls();

        await WithDbAsync(async db =>
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == TenantB);
            tenant.IsActive = false;
        });

        await provider.RefreshAsync();

        _notifier.Received(1).Notify();
        Assert.Null(provider.GetTenant(TenantB));
        Assert.Equal(1, provider.ActiveTenantCount);
    }

    [Fact]
    public async Task RefreshAsync_FrontendOriginAdded_Notifies()
    {
        await WithDbAsync(db =>
        {
            db.Tenants.Add(Tenant(TenantA, "A", """{"ClientId":"client-a"}""", "https://a.example.com"));
            return Task.CompletedTask;
        });

        var provider = CreateProvider();
        await provider.RefreshAsync();
        _notifier.ClearReceivedCalls();

        await WithDbAsync(db =>
        {
            db.TenantFrontendOrigins.Add(new TenantFrontendOriginEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                Origin = "https://a2.example.com"
            });
            return Task.CompletedTask;
        });

        await provider.RefreshAsync();

        _notifier.Received(1).Notify();
        Assert.NotNull(provider.GetTenantByOrigin("https://a2.example.com"));
    }

    [Fact]
    public async Task RefreshAsync_ValueMovedBetweenAdjacentFields_IsDetectedAsChange()
    {
        await WithDbAsync(db =>
        {
            db.Tenants.Add(Tenant(TenantA, "A", """{"Ab":"c"}"""));
            return Task.CompletedTask;
        });

        var provider = CreateProvider();
        await provider.RefreshAsync();
        _notifier.ClearReceivedCalls();

        await WithDbAsync(async db =>
        {
            var setting = await db.TenantSettings.SingleAsync(s => s.TenantId == TenantA);
            setting.Settings = """{"A":"bc"}""";
        });

        await provider.RefreshAsync();

        _notifier.Received(1).Notify();
    }
}
