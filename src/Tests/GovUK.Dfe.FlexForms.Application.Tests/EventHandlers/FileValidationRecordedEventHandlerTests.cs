using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Models;
using GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.NSubstitute;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.EventHandlers;

public class FileValidationRecordedEventHandlerTests
{
    [Fact]
    public async Task Handle_SendsErrorNotification_WhenValidationFailed()
    {
        var user = CreateUser("uploader@example.com");
        var userRepo = Substitute.For<IEaRepository<User>>();
        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var notificationService = Substitute.For<INotificationService>();
        notificationService.AddNotificationAsync(
                Arg.Any<string>(),
                Arg.Any<NotificationType>(),
                Arg.Any<NotificationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => new Notification
            {
                Id = "n-1",
                Message = ci.ArgAt<string>(0),
                Type = ci.ArgAt<NotificationType>(1),
                UserId = "uploader@example.com",
                CreatedAt = DateTime.UtcNow
            });

        var signalR = new MockNotificationSignalRService();
        var handler = new FileValidationRecordedEventHandler(
            NullLogger<FileValidationRecordedEventHandler>.Instance,
            userRepo,
            notificationService,
            signalR);

        var fileId = new FileId(Guid.NewGuid());
        await handler.Handle(
            new FileValidationRecordedEvent(
                fileId,
                new ApplicationId(Guid.NewGuid()),
                FileValidationStatus.Failed,
                "Missing Amount column",
                "budget.xlsx",
                user.Id!,
                DateTime.UtcNow),
            CancellationToken.None);

        await notificationService.Received(1).AddNotificationAsync(
            Arg.Is<string>(m => m.Contains("budget.xlsx") && m.Contains("Missing Amount column")),
            NotificationType.Error,
            Arg.Is<NotificationOptions>(o =>
                o.Category == FileValidationRecordedEventHandler.Category
                && o.UserId == "uploader@example.com"
                && o.AutoDismiss == false),
            Arg.Any<CancellationToken>());

        Assert.Single(signalR.SentNotifications);
        Assert.Equal("n-1", signalR.SentNotifications[0].Id);
        Assert.Equal(FileValidationRecordedEventHandler.Category, signalR.SentNotifications[0].Category);
    }

    [Fact]
    public async Task Handle_SendsSuccessNotification_WhenValidationPassed()
    {
        var user = CreateUser("uploader@example.com");
        var userRepo = Substitute.For<IEaRepository<User>>();
        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var notificationService = Substitute.For<INotificationService>();
        notificationService.AddNotificationAsync(
                Arg.Any<string>(),
                Arg.Any<NotificationType>(),
                Arg.Any<NotificationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => new Notification
            {
                Id = "n-2",
                Message = ci.ArgAt<string>(0),
                Type = ci.ArgAt<NotificationType>(1),
                UserId = "uploader@example.com",
                CreatedAt = DateTime.UtcNow
            });

        var signalR = new MockNotificationSignalRService();
        var handler = new FileValidationRecordedEventHandler(
            NullLogger<FileValidationRecordedEventHandler>.Instance,
            userRepo,
            notificationService,
            signalR);

        await handler.Handle(
            new FileValidationRecordedEvent(
                new FileId(Guid.NewGuid()),
                new ApplicationId(Guid.NewGuid()),
                FileValidationStatus.Passed,
                null,
                "budget.xlsx",
                user.Id!,
                DateTime.UtcNow),
            CancellationToken.None);

        await notificationService.Received(1).AddNotificationAsync(
            Arg.Is<string>(m => m.Contains("validated")),
            NotificationType.Success,
            Arg.Is<NotificationOptions>(o => o.AutoDismiss == true),
            Arg.Any<CancellationToken>());

        Assert.Single(signalR.SentNotifications);
    }

    [Fact]
    public async Task Handle_DoesNotThrow_WhenUploaderMissing()
    {
        var userRepo = Substitute.For<IEaRepository<User>>();
        var users = Array.Empty<User>().AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var notificationService = Substitute.For<INotificationService>();
        var handler = new FileValidationRecordedEventHandler(
            NullLogger<FileValidationRecordedEventHandler>.Instance,
            userRepo,
            notificationService,
            new MockNotificationSignalRService());

        await handler.Handle(
            new FileValidationRecordedEvent(
                new FileId(Guid.NewGuid()),
                new ApplicationId(Guid.NewGuid()),
                FileValidationStatus.Failed,
                "bad",
                "file.xlsx",
                new UserId(Guid.NewGuid()),
                DateTime.UtcNow),
            CancellationToken.None);

        await notificationService.DidNotReceive().AddNotificationAsync(
            Arg.Any<string>(),
            Arg.Any<NotificationType>(),
            Arg.Any<NotificationOptions>(),
            Arg.Any<CancellationToken>());
    }

    private static User CreateUser(string email) =>
        new(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Uploader",
            email,
            DateTime.UtcNow,
            null,
            null,
            null);
}
