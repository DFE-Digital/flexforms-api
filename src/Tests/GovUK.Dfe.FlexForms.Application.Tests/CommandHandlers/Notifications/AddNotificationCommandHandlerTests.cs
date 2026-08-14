using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Notifications.Commands;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MockQueryable;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using System.Security.Claims;
using NSubstitute.ExceptionExtensions;
using Xunit;
using GovUK.Dfe.FlexForms.Tests.Common.Mocks;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Notifications;

public class AddNotificationCommandHandlerTests
{
    private readonly INotificationService _notificationService;
    private readonly IPermissionCheckerService _permissionCheckerService;
    private readonly INotificationSignalRService _notificationSignalRService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IEaRepository<User> _userRepository;
    private readonly AddNotificationCommandHandler _handler;

    public AddNotificationCommandHandlerTests()
    {
        _notificationService = Substitute.For<INotificationService>();
        _permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        _notificationSignalRService = new MockNotificationSignalRService();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _userRepository = Substitute.For<IEaRepository<User>>();
        _userRepository.Query().Returns(new List<User>().AsQueryable().BuildMock());

        _handler = new AddNotificationCommandHandler(
            _notificationService,
            _permissionCheckerService,
            _notificationSignalRService,
            _httpContextAccessor,
            _userRepository);
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ShouldCreateNotification_WhenValidRequestAndUserHasPermission(
        AddNotificationCommand command,
        Notification notification)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _permissionCheckerService.HasPermission(ResourceType.Notifications, email, AccessType.Write).Returns(true);

        // Set up the notification to return from the service
        notification.Id = Guid.NewGuid().ToString();
        notification.Message = command.Message;
        notification.Type = command.Type;
        notification.UserId = email;
        notification.CreatedAt = DateTime.UtcNow;
        notification.IsRead = false;
        notification.AutoDismiss = command.AutoDismiss ?? true;
        notification.AutoDismissSeconds = command.AutoDismissSeconds ?? 5;
        notification.Category = command.Category;
        notification.Context = command.Context;
        notification.ActionUrl = command.ActionUrl;
        notification.Metadata = command.Metadata;
        notification.Priority = command.Priority ?? NotificationPriority.Normal;

        _notificationService.AddNotificationAsync(
                command.Message,
                command.Type,
                Arg.Any<NotificationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(notification);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(command.Message, result.Value.Message);
        Assert.Equal(command.Type, result.Value.Type);
        Assert.Equal(email, result.Value.UserId);

        await _notificationService.Received(1).AddNotificationAsync(
            command.Message,
            command.Type,
            Arg.Is<NotificationOptions>(opts => 
                opts.UserId == email &&
                opts.Category == command.Category &&
                opts.Context == command.Context &&
                opts.AutoDismiss == (command.AutoDismiss ?? true) &&
                opts.AutoDismissSeconds == (command.AutoDismissSeconds ?? 5) &&
                opts.ActionUrl == command.ActionUrl &&
                opts.Metadata == command.Metadata &&
                opts.Priority == (command.Priority ?? NotificationPriority.Normal) &&
                opts.ReplaceExistingContext == (command.ReplaceExistingContext ?? true)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ShouldReturnForbidden_WhenUserDoesNotHavePermission(
        AddNotificationCommand command)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _permissionCheckerService.HasPermission(ResourceType.Notifications, email, AccessType.Write).Returns(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User does not have permission to create notifications", result.Error);

        await _notificationService.DidNotReceive().AddNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<NotificationType>(),
            Arg.Any<NotificationOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ShouldReturnForbidden_WhenUserNotAuthenticated(
        AddNotificationCommand command)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // Not authenticated
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);

        await _notificationService.DidNotReceive().AddNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<NotificationType>(),
            Arg.Any<NotificationOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ShouldReturnForbidden_WhenNoUserIdentifier(
        AddNotificationCommand command)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>(); // No email or other identifier claims
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("No user identifier", result.Error);

        await _notificationService.DidNotReceive().AddNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<NotificationType>(),
            Arg.Any<NotificationOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ShouldUseAppIdAsUserId_WhenEmailNotAvailable(
        AddNotificationCommand command,
        Notification notification)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var appId = "test-app-id";
        var claims = new List<Claim>
        {
            new("appid", appId)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _permissionCheckerService.HasPermission(ResourceType.Notifications, appId, AccessType.Write).Returns(true);

        command = command with { ToUserId = null };

        notification.UserId = appId;
        _notificationService.AddNotificationAsync(
                Arg.Any<string>(),
                Arg.Any<NotificationType>(),
                Arg.Any<NotificationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(notification);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _permissionCheckerService.Received(1).HasPermission(ResourceType.Notifications, appId, AccessType.Write);
        await _notificationService.Received(1).AddNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<NotificationType>(),
            Arg.Is<NotificationOptions>(opts => opts.UserId == appId),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ShouldReturnFailure_WhenExceptionThrown(
        AddNotificationCommand command)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _permissionCheckerService.HasPermission(ResourceType.Notifications, email, AccessType.Write).Returns(true);

        var exceptionMessage = "Test exception";
        _notificationService.AddNotificationAsync(
                Arg.Any<string>(),
                Arg.Any<NotificationType>(),
                Arg.Any<NotificationOptions>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception(exceptionMessage));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(exceptionMessage, result.Error);
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ShouldSendSignalRNotification_WhenNotificationCreatedSuccessfully(
        AddNotificationCommand command,
        Notification notification)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _permissionCheckerService.HasPermission(ResourceType.Notifications, email, AccessType.Write).Returns(true);

        // Set up the notification to return from the service
        notification.Id = Guid.NewGuid().ToString();
        notification.Message = command.Message;
        notification.Type = command.Type;
        notification.UserId = email;
        notification.CreatedAt = DateTime.UtcNow;
        notification.IsRead = false;
        notification.AutoDismiss = command.AutoDismiss ?? true;
        notification.AutoDismissSeconds = command.AutoDismissSeconds ?? 5;
        notification.Category = command.Category;
        notification.Context = command.Context;
        notification.ActionUrl = command.ActionUrl;
        notification.Metadata = command.Metadata;
        notification.Priority = command.Priority ?? NotificationPriority.Normal;

        _notificationService.AddNotificationAsync(
                command.Message,
                command.Type,
                Arg.Any<NotificationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(notification);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var mockService = (MockNotificationSignalRService)_notificationSignalRService;
        Assert.Single(mockService.SentNotifications);
        var sentNotification = mockService.SentNotifications.First();
        Assert.NotNull(sentNotification);
    }

    [Fact]
    public async Task Handle_ShouldTargetToUser_WhenCallerIsServicePrincipal()
    {
        var targetUserId = new UserId(Guid.NewGuid());
        var targetUser = new User(
            targetUserId,
            new RoleId(Guid.NewGuid()),
            "Target User",
            "uploader@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        var httpContext = new DefaultHttpContext();
        var appId = "scan-consumer-app";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("appid", appId),
            new Claim(TenantAuthClaimTypes.IsService, "true")
        ], "TestAuth"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _permissionCheckerService.HasPermission(ResourceType.Notifications, appId, AccessType.Write).Returns(true);
        _permissionCheckerService.IsAdmin().Returns(false);
        _userRepository.Query().Returns(new List<User> { targetUser }.AsQueryable().BuildMock());

        var notification = new Notification
        {
            Id = Guid.NewGuid().ToString(),
            Message = "infected",
            Type = NotificationType.Error,
            UserId = targetUser.Email,
            CreatedAt = DateTime.UtcNow
        };

        _notificationService.AddNotificationAsync(
                Arg.Any<string>(),
                Arg.Any<NotificationType>(),
                Arg.Any<NotificationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(notification);

        var command = new AddNotificationCommand(
            "infected",
            NotificationType.Error,
            ToUserId: targetUserId);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _notificationService.Received(1).AddNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<NotificationType>(),
            Arg.Is<NotificationOptions>(opts => opts.UserId == "uploader@example.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotTargetToUser_WhenInteractiveCallerIsNotAdmin()
    {
        var targetUserId = new UserId(Guid.NewGuid());
        var email = "user@example.com";
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Email, email)
        ], "TestAuth"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _permissionCheckerService.HasPermission(ResourceType.Notifications, email, AccessType.Write).Returns(true);
        _permissionCheckerService.IsAdmin().Returns(false);

        var notification = new Notification
        {
            Id = Guid.NewGuid().ToString(),
            Message = "hello",
            Type = NotificationType.Info,
            UserId = email,
            CreatedAt = DateTime.UtcNow
        };

        _notificationService.AddNotificationAsync(
                Arg.Any<string>(),
                Arg.Any<NotificationType>(),
                Arg.Any<NotificationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(notification);

        var command = new AddNotificationCommand(
            "hello",
            NotificationType.Info,
            ToUserId: targetUserId);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _notificationService.Received(1).AddNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<NotificationType>(),
            Arg.Is<NotificationOptions>(opts => opts.UserId == email),
            Arg.Any<CancellationToken>());
        _userRepository.DidNotReceive().Query();
    }
}
