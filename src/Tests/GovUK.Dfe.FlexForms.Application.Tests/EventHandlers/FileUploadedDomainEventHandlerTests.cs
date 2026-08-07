using GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.EventHandlers;

public class FileUploadedDomainEventHandlerTests
{
    private readonly ILogger<FileUploadedDomainEventHandler> _logger;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IAzureSpecificOperations _azureOps;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IEaRepository<User> _userRepository;
    private readonly IEventTriggerDispatcher _eventTriggerDispatcher;
    private readonly FileUploadedDomainEventHandler _handler;

    public FileUploadedDomainEventHandlerTests()
    {
        _logger = Substitute.For<ILogger<FileUploadedDomainEventHandler>>();
        _eventPublisher = Substitute.For<IEventPublisher>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        _azureOps = Substitute.For<IAzureSpecificOperations>();
        _applicationRepository = Substitute.For<IApplicationRepository>();
        _userRepository = Substitute.For<IEaRepository<User>>();
        _eventTriggerDispatcher = Substitute.For<IEventTriggerDispatcher>();

        _applicationRepository.Query().Returns(Array.Empty<Domain.Entities.Application>().AsQueryable());
        _userRepository.Query().Returns(Array.Empty<User>().AsQueryable());

        _handler = new FileUploadedDomainEventHandler(
            _logger,
            _eventPublisher,
            _tenantContextAccessor,
            _azureOps,
            _applicationRepository,
            _userRepository,
            _eventTriggerDispatcher);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenTenantContextIsNull()
    {
        // Arrange
        _tenantContextAccessor.CurrentTenant.Returns((TenantConfiguration?)null);

        var file = CreateFileWithApplication();
        var @event = new FileUploadedDomainEvent(file, "hash123", DateTime.UtcNow);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(@event, CancellationToken.None));
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public async Task Handle_ShouldPublishEvent_WhenTenantContextIsAvailable(
        File file, long fileSize)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenantConfig = new TenantConfiguration(
            tenantId, "TestTenant",
            new ConfigurationBuilder().Build(),
            Array.Empty<string>());
        _tenantContextAccessor.CurrentTenant.Returns(tenantConfig);

        var fileWithApp = CreateFileWithApplication();
        var @event = new FileUploadedDomainEvent(fileWithApp, "hash-abc", DateTime.UtcNow);

        _azureOps.GenerateSasTokenAsync(
                Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://blob.azure.com/sas-token");

        // Act
        await _handler.Handle(@event, CancellationToken.None);

        // Assert
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Any<GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events.ScanRequestedEvent>(),
            Arg.Any<GovUK.Dfe.CoreLibs.Messaging.MassTransit.Models.AzureServiceBusMessageProperties>(),
            Arg.Any<CancellationToken>());
    }

    private static File CreateFileWithApplication()
    {
        var applicationId = new ApplicationId(Guid.NewGuid());
        var fileId = new FileId(Guid.NewGuid());
        var uploadedBy = new UserId(Guid.NewGuid());

        var application = new Domain.Entities.Application(
            applicationId,
            "APP-REF-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            uploadedBy,
            GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.ApplicationStatus.InProgress,
            null, null);

        var file = new File(
            fileId,
            applicationId,
            "TestFile",
            "Description",
            "original.pdf",
            "hashed.pdf",
            "APP-REF-001",
            DateTime.UtcNow,
            uploadedBy,
            1024);

        // Set the Application navigation property via reflection
        var appProp = typeof(File).GetProperty("Application");
        appProp?.SetValue(file, application);

        return file;
    }
}
