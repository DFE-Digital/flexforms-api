using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Models;
using GovUK.Dfe.FlexForms.Application.Notifications;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Notifications;

public class TenantScopedNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly INotificationService _inner = Substitute.For<INotificationService>();
    private readonly TenantScopedNotificationService _sut;

    public TenantScopedNotificationServiceTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(new TenantConfiguration(_tenantId, "Transfers", config, Array.Empty<string>()));
        _sut = new TenantScopedNotificationService(_inner, accessor);
    }

    [Fact]
    public async Task GetUnread_ScopesUserIdWithTenant()
    {
        _inner.GetUnreadNotificationsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.GetUnreadNotificationsAsync("user@example.com", "Transfers", null, CancellationToken.None);

        await _inner.Received(1).GetUnreadNotificationsAsync(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee:user@example.com",
            "Transfers",
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddNotification_ScopesOptionsUserId_AndStripsOnReturn()
    {
        var stored = new Notification
        {
            UserId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee:user@example.com",
            Message = "Hello"
        };
        _inner.AddNotificationAsync(Arg.Any<string>(), Arg.Any<NotificationType>(), Arg.Any<NotificationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(stored);

        var result = await _sut.AddNotificationAsync(
            "Hello",
            NotificationType.Info,
            new NotificationOptions { UserId = "user@example.com" },
            CancellationToken.None);

        await _inner.Received(1).AddNotificationAsync(
            "Hello",
            NotificationType.Info,
            Arg.Is<NotificationOptions>(o => o.UserId == "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee:user@example.com"),
            Arg.Any<CancellationToken>());
        Assert.Equal("user@example.com", result.UserId);
    }
}
