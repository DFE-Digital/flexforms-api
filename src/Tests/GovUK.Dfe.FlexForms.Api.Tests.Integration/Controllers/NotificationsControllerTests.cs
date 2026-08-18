using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Notifications.Models;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

public class NotificationsControllerTests
{
    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateNotificationAsync_ShouldCreateNotification_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new AddNotificationRequest
        {
            Message = "Test notification message",
            Type = NotificationType.Info,
            Category = "Test",
            Context = "Testing",
            AutoDismiss = true,
            AutoDismissSeconds = 30,
            Priority = NotificationPriority.Normal,
            ActionUrl = "/test-action"
        };

        // Act
        var result = await notificationsClient.CreateNotificationAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Id);
        Assert.Equal("Test notification message", result.Message);
        Assert.Equal(NotificationType.Info, result.Type);
        Assert.Equal("Test", result.Category);
        Assert.Equal("Testing", result.Context);
        Assert.True(result.AutoDismiss);
        Assert.Equal(30, result.AutoDismissSeconds);
        Assert.False(result.IsRead);
        Assert.Equal(NotificationPriority.Normal, result.Priority);
        Assert.Equal("/test-action", result.ActionUrl);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateNotificationAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient)
    {
        // Arrange
        var request = new AddNotificationRequest
        {
            Message = "Test notification message",
            Type = NotificationType.Info
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.CreateNotificationAsync(request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateNotificationAsync_ShouldReturnBadRequest_WhenInvalidData(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new AddNotificationRequest
        {
            Message = "", // Invalid - empty message
            Type = NotificationType.Info // Valid type, but message is invalid
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.CreateNotificationAsync(request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetUnreadNotificationsAsync_ShouldReturnNotifications_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // First create a notification
        var createRequest = new AddNotificationRequest
        {
            Message = "Test notification for unread test",
            Type = NotificationType.Info
        };

        await notificationsClient.CreateNotificationAsync(createRequest);

        // Act
        var result = await notificationsClient.GetUnreadNotificationsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.All(result, n => Assert.False(n.IsRead));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetAllNotificationsAsync_ShouldReturnAllNotifications_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await notificationsClient.GetAllNotificationsAsync();

        // Assert
        Assert.NotNull(result);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetNotificationsByCategoryAsync_ShouldReturnFilteredNotifications_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Read"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Create a notification with specific category
        var createRequest = new AddNotificationRequest
        {
            Message = "Test notification for category filter",
            Type = NotificationType.Info,
            Category = "TestCategory"
        };

        await notificationsClient.CreateNotificationAsync(createRequest);

        // Act
        var result = await notificationsClient.GetNotificationsByCategoryAsync("TestCategory");

        // Assert
        Assert.NotNull(result);
        Assert.All(result, n => Assert.Equal("TestCategory", n.Category));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetUnreadNotificationCountAsync_ShouldReturnCount_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await notificationsClient.GetUnreadNotificationCountAsync();

        // Assert
        Assert.True(result >= 0);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task MarkNotificationAsReadAsync_ShouldMarkAsRead_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // First create a notification
        var createRequest = new AddNotificationRequest
        {
            Message = "Test notification for marking as read",
            Type = NotificationType.Info
        };

        var createdNotification = await notificationsClient.CreateNotificationAsync(createRequest);

        // Act
        var result = await notificationsClient.MarkNotificationAsReadAsync(createdNotification.Id);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task MarkAllNotificationsAsReadAsync_ShouldMarkAllAsRead_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await notificationsClient.MarkAllNotificationsAsReadAsync();

        // Assert
        Assert.True(result);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RemoveNotificationAsync_ShouldRemoveNotification_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // First create a notification
        var createRequest = new AddNotificationRequest
        {
            Message = "Test notification for removal",
            Type = NotificationType.Info
        };

        var createdNotification = await notificationsClient.CreateNotificationAsync(createRequest);

        // Act
        var result = await notificationsClient.RemoveNotificationAsync(createdNotification.Id);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task ClearAllNotificationsAsync_ShouldClearAll_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await notificationsClient.ClearAllNotificationsAsync();

        // Assert
        Assert.True(result);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task ClearNotificationsByCategoryAsync_ShouldClearByCategory_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await notificationsClient.ClearNotificationsByCategoryAsync("TestCategory");

        // Assert
        Assert.True(result);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task ClearNotificationsByContextAsync_ShouldClearByContext_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Write"),
            new("permission", $"Notifications:{EaContextSeeder.BobEmail}:Delete")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await notificationsClient.ClearNotificationsByContextAsync("TestContext");

        // Assert
        Assert.True(result);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task NotificationEndpoints_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        INotificationsClient notificationsClient)
    {
        // Test all endpoints without authorization
        var ex1 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.GetAllNotificationsAsync());
        Assert.Equal(403, ex1.StatusCode);

        var ex2 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.GetUnreadNotificationsAsync());
        Assert.Equal(403, ex2.StatusCode);

        var ex3 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.GetUnreadNotificationCountAsync());
        Assert.Equal(403, ex3.StatusCode);

        var ex4 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.GetNotificationsByCategoryAsync("test"));
        Assert.Equal(403, ex4.StatusCode);

        var ex5 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.MarkNotificationAsReadAsync("test-id"));
        Assert.Equal(403, ex5.StatusCode);

        var ex6 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.MarkAllNotificationsAsReadAsync());
        Assert.Equal(403, ex6.StatusCode);

        var ex7 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.RemoveNotificationAsync("test-id"));
        Assert.Equal(403, ex7.StatusCode);

        var ex8 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.ClearAllNotificationsAsync());
        Assert.Equal(403, ex8.StatusCode);

        var ex9 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.ClearNotificationsByCategoryAsync("test"));
        Assert.Equal(403, ex9.StatusCode);

        var ex10 = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => notificationsClient.ClearNotificationsByContextAsync("test"));
        Assert.Equal(403, ex10.StatusCode);
    }
}
