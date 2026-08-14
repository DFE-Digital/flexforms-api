using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Models;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Application.Notifications;

/// <summary>
/// Prefixes notification storage user keys with the current tenant so the same email
/// does not share a Redis list across tenants.
/// </summary>
public sealed class TenantScopedNotificationService(
    INotificationService inner,
    ITenantContextAccessor tenantContextAccessor) : INotificationService
{
    public Task AddSuccessAsync(string message, NotificationOptions? options = null, CancellationToken cancellationToken = default) =>
        inner.AddSuccessAsync(message, ScopeOptions(options), cancellationToken);

    public Task AddErrorAsync(string message, NotificationOptions? options = null, CancellationToken cancellationToken = default) =>
        inner.AddErrorAsync(message, ScopeOptions(options), cancellationToken);

    public Task AddInfoAsync(string message, NotificationOptions? options = null, CancellationToken cancellationToken = default) =>
        inner.AddInfoAsync(message, ScopeOptions(options), cancellationToken);

    public Task AddWarningAsync(string message, NotificationOptions? options = null, CancellationToken cancellationToken = default) =>
        inner.AddWarningAsync(message, ScopeOptions(options), cancellationToken);

    public async Task<Notification> AddNotificationAsync(
        string message,
        NotificationType type,
        NotificationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var notification = await inner.AddNotificationAsync(message, type, ScopeOptions(options), cancellationToken);
        Unscope(notification);
        return notification;
    }

    public async Task<IEnumerable<Notification>> GetUnreadNotificationsAsync(
        string? userId = null,
        string? context = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var notifications = await inner.GetUnreadNotificationsAsync(Scope(userId), context, category, cancellationToken);
        return UnscopeAll(notifications);
    }

    public async Task<IEnumerable<Notification>> GetAllNotificationsAsync(
        string? userId = null,
        string? context = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var notifications = await inner.GetAllNotificationsAsync(Scope(userId), context, category, cancellationToken);
        return UnscopeAll(notifications);
    }

    public async Task<IEnumerable<Notification>> GetNotificationsByCategoryAsync(
        string category,
        bool unreadOnly = false,
        string? userId = null,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        var notifications = await inner.GetNotificationsByCategoryAsync(
            category,
            unreadOnly,
            Scope(userId),
            context,
            cancellationToken);
        return UnscopeAll(notifications);
    }

    public async Task<IEnumerable<Notification>> GetNotificationsByContextAsync(
        string context,
        bool unreadOnly = false,
        string? userId = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var notifications = await inner.GetNotificationsByContextAsync(
            context,
            unreadOnly,
            Scope(userId),
            category,
            cancellationToken);
        return UnscopeAll(notifications);
    }

    public Task MarkAsReadAsync(string notificationId, string? userId = null, CancellationToken cancellationToken = default) =>
        inner.MarkAsReadAsync(notificationId, Scope(userId), cancellationToken);

    public Task MarkAllAsReadAsync(
        string? userId = null,
        string? context = null,
        string? category = null,
        CancellationToken cancellationToken = default) =>
        inner.MarkAllAsReadAsync(Scope(userId), context, category, cancellationToken);

    public Task RemoveNotificationAsync(string notificationId, string? userId = null, CancellationToken cancellationToken = default) =>
        inner.RemoveNotificationAsync(notificationId, Scope(userId), cancellationToken);

    public Task ClearAllNotificationsAsync(string? userId = null, CancellationToken cancellationToken = default) =>
        inner.ClearAllNotificationsAsync(Scope(userId), cancellationToken);

    public Task ClearNotificationsByCategoryAsync(
        string category,
        string? userId = null,
        CancellationToken cancellationToken = default) =>
        inner.ClearNotificationsByCategoryAsync(category, Scope(userId), cancellationToken);

    public Task ClearNotificationsByContextAsync(
        string context,
        string? userId = null,
        CancellationToken cancellationToken = default) =>
        inner.ClearNotificationsByContextAsync(context, Scope(userId), cancellationToken);

    public Task<int> GetUnreadCountAsync(
        string? userId = null,
        string? context = null,
        string? category = null,
        CancellationToken cancellationToken = default) =>
        inner.GetUnreadCountAsync(Scope(userId), context, category, cancellationToken);

    private string Scope(string? userId)
    {
        var tenantId = tenantContextAccessor.CurrentTenant?.Id
            ?? throw new InvalidOperationException("Tenant context is required to access notifications.");

        return string.IsNullOrWhiteSpace(userId)
            ? tenantId.ToString("D")
            : TenantScopedIdentityKey.Combine(tenantId, userId);
    }

    private NotificationOptions ScopeOptions(NotificationOptions? options)
    {
        options ??= new NotificationOptions();
        options.UserId = Scope(options.UserId);
        return options;
    }

    private static IEnumerable<Notification> UnscopeAll(IEnumerable<Notification> notifications)
    {
        foreach (var notification in notifications)
            Unscope(notification);

        return notifications;
    }

    private static void Unscope(Notification notification)
    {
        if (notification.UserId is not null
            && TenantScopedIdentityKey.TrySplit(notification.UserId, out _, out var identity))
        {
            notification.UserId = identity;
        }
    }
}
