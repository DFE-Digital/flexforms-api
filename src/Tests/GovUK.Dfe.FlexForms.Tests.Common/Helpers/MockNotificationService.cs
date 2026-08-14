using System.Collections.Concurrent;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Models;

namespace GovUK.Dfe.FlexForms.Tests.Common.Helpers;

/// <summary>
/// Mock notification service that stores notifications in memory for testing purposes
/// </summary>
public class MockNotificationService : GovUK.Dfe.CoreLibs.Notifications.Interfaces.INotificationService
{
    private readonly ConcurrentDictionary<string, List<Notification>> _userNotifications = new();
    private int _notificationIdCounter = 1;

    public async Task AddSuccessAsync(string message, NotificationOptions? options = null, CancellationToken cancellationToken = default)
    {
        await AddNotificationAsync(message, NotificationType.Success, options, cancellationToken);
    }

    public async Task AddErrorAsync(string message, NotificationOptions? options = null, CancellationToken cancellationToken = default)
    {
        await AddNotificationAsync(message, NotificationType.Error, options, cancellationToken);
    }

    public async Task AddInfoAsync(string message, NotificationOptions? options = null, CancellationToken cancellationToken = default)
    {
        await AddNotificationAsync(message, NotificationType.Info, options, cancellationToken);
    }

    public async Task AddWarningAsync(string message, NotificationOptions? options = null, CancellationToken cancellationToken = default)
    {
        await AddNotificationAsync(message, NotificationType.Warning, options, cancellationToken);
    }

    public Task<Notification> AddNotificationAsync(string message, NotificationType type, NotificationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var userId = options?.UserId ?? "default-user";
        var notificationId = _notificationIdCounter++.ToString();
        
        var notification = new Notification
        {
            Id = notificationId,
            Message = message,
            Type = type,
            Category = options?.Category,
            Context = options?.Context,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            AutoDismiss = options?.AutoDismiss ?? true,
            AutoDismissSeconds = options?.AutoDismissSeconds ?? 5,
            UserId = userId,
            ActionUrl = options?.ActionUrl,
            Metadata = options?.Metadata,
            Priority = options?.Priority ?? NotificationPriority.Normal
        };

        _userNotifications.AddOrUpdate(userId, 
            new List<Notification> { notification },
            (key, existing) => 
            {
                existing.Add(notification);
                return existing;
            });

        return Task.FromResult(notification);
    }

    public Task<IEnumerable<Notification>> GetUnreadNotificationsAsync(string? userId = null, string? context = null, string? category = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult<IEnumerable<Notification>>(new List<Notification>());
        }

        if (_userNotifications.TryGetValue(userId, out var notifications))
        {
            var unreadNotifications = notifications.Where(n => !n.IsRead);
            
            if (!string.IsNullOrEmpty(context))
                unreadNotifications = unreadNotifications.Where(n => n.Context == context);
            
            if (!string.IsNullOrEmpty(category))
                unreadNotifications = unreadNotifications.Where(n => n.Category == category);
            
            return Task.FromResult<IEnumerable<Notification>>(unreadNotifications);
        }

        return Task.FromResult<IEnumerable<Notification>>(new List<Notification>());
    }

    public Task<IEnumerable<Notification>> GetAllNotificationsAsync(string? userId = null, string? context = null, string? category = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult<IEnumerable<Notification>>(new List<Notification>());
        }

        if (_userNotifications.TryGetValue(userId, out var notifications))
        {
            IEnumerable<Notification> result = notifications;
            
            if (!string.IsNullOrEmpty(context))
                result = result.Where(n => n.Context == context);
            
            if (!string.IsNullOrEmpty(category))
                result = result.Where(n => n.Category == category);
            
            return Task.FromResult<IEnumerable<Notification>>(result);
        }

        return Task.FromResult<IEnumerable<Notification>>(new List<Notification>());
    }

    public Task<IEnumerable<Notification>> GetNotificationsByCategoryAsync(string category, bool unreadOnly = false, string? userId = null, string? context = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult<IEnumerable<Notification>>(new List<Notification>());
        }

        if (_userNotifications.TryGetValue(userId, out var notifications))
        {
            var filteredNotifications = notifications.Where(n => n.Category == category);
            
            if (!string.IsNullOrEmpty(context))
                filteredNotifications = filteredNotifications.Where(n => n.Context == context);
            
            if (unreadOnly)
            {
                filteredNotifications = filteredNotifications.Where(n => !n.IsRead);
            }

            return Task.FromResult<IEnumerable<Notification>>(filteredNotifications);
        }

        return Task.FromResult<IEnumerable<Notification>>(new List<Notification>());
    }

    public Task<IEnumerable<Notification>> GetNotificationsByContextAsync(string context, bool unreadOnly = false, string? userId = null, string? category = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult<IEnumerable<Notification>>(new List<Notification>());
        }

        if (_userNotifications.TryGetValue(userId, out var notifications))
        {
            var filteredNotifications = notifications.Where(n => n.Context == context);
            
            if (!string.IsNullOrEmpty(category))
                filteredNotifications = filteredNotifications.Where(n => n.Category == category);
            
            if (unreadOnly)
            {
                filteredNotifications = filteredNotifications.Where(n => !n.IsRead);
            }

            return Task.FromResult<IEnumerable<Notification>>(filteredNotifications);
        }

        return Task.FromResult<IEnumerable<Notification>>(new List<Notification>());
    }

    public Task MarkAsReadAsync(string notificationId, string? userId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(userId) && _userNotifications.TryGetValue(userId, out var scopedNotifications))
        {
            var notification = scopedNotifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
                notification.IsRead = true;

            return Task.CompletedTask;
        }

        foreach (var userNotifications in _userNotifications.Values)
        {
            var notification = userNotifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
                break;
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkAllAsReadAsync(string? userId = null, string? context = null, string? category = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(userId) && _userNotifications.TryGetValue(userId, out var notifications))
        {
            var toMark = notifications.AsEnumerable();
            
            if (!string.IsNullOrEmpty(context))
                toMark = toMark.Where(n => n.Context == context);
            
            if (!string.IsNullOrEmpty(category))
                toMark = toMark.Where(n => n.Category == category);
            
            foreach (var notification in toMark)
            {
                notification.IsRead = true;
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveNotificationAsync(string notificationId, string? userId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(userId) && _userNotifications.TryGetValue(userId, out var scopedNotifications))
        {
            var notification = scopedNotifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
                scopedNotifications.Remove(notification);

            return Task.CompletedTask;
        }

        foreach (var userNotifications in _userNotifications.Values)
        {
            var notification = userNotifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
            {
                userNotifications.Remove(notification);
                break;
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearAllNotificationsAsync(string? userId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(userId) && _userNotifications.TryGetValue(userId, out var notifications))
        {
            notifications.Clear();
        }

        return Task.CompletedTask;
    }

    public Task ClearNotificationsByCategoryAsync(string category, string? userId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(userId) && _userNotifications.TryGetValue(userId, out var notifications))
        {
            var toRemove = notifications.Where(n => n.Category == category).ToList();
            foreach (var notification in toRemove)
            {
                notifications.Remove(notification);
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearNotificationsByContextAsync(string context, string? userId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(userId) && _userNotifications.TryGetValue(userId, out var notifications))
        {
            var toRemove = notifications.Where(n => n.Context == context).ToList();
            foreach (var notification in toRemove)
            {
                notifications.Remove(notification);
            }
        }

        return Task.CompletedTask;
    }

    public Task<int> GetUnreadCountAsync(string? userId = null, string? context = null, string? category = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(0);
        }

        if (_userNotifications.TryGetValue(userId, out var notifications))
        {
            var unread = notifications.Where(n => !n.IsRead);
            
            if (!string.IsNullOrEmpty(context))
                unread = unread.Where(n => n.Context == context);
            
            if (!string.IsNullOrEmpty(category))
                unread = unread.Where(n => n.Category == category);
            
            return Task.FromResult(unread.Count());
        }

        return Task.FromResult(0);
    }
}