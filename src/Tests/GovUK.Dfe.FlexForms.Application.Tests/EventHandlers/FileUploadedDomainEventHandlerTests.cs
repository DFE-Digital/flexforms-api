using GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Models;
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
    private readonly ITenantAzureFileStorageFactory _tenantAzureFactory;
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
        _tenantAzureFactory = Substitute.For<ITenantAzureFileStorageFactory>();
        _azureOps = Substitute.For<IAzureSpecificOperations>();
        _applicationRepository = Substitute.For<IApplicationRepository>();
        _userRepository = Substitute.For<IEaRepository<User>>();
        _eventTriggerDispatcher = Substitute.For<IEventTriggerDispatcher>();

        _applicationRepository.Query().Returns(Array.Empty<Domain.Entities.Application>().AsQueryable());
        _userRepository.Query().Returns(Array.Empty<User>().AsQueryable());
        _tenantAzureFactory.GetAzureOperationsOrNull().Returns((IAzureSpecificOperations?)null);

        _handler = new FileUploadedDomainEventHandler(
            _logger,
            _eventPublisher,
            _tenantContextAccessor,
            _tenantAzureFactory,
            [_azureOps],
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
            Arg.Is<GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events.ScanRequestedEvent>(e =>
                e.Metadata != null
                && e.Metadata.ContainsKey("TenantId")
                && e.Metadata.ContainsKey("userId")
                && e.Metadata.ContainsKey("templateId")),
            Arg.Any<GovUK.Dfe.CoreLibs.Messaging.MassTransit.Models.AzureServiceBusMessageProperties>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUseSas_WhenAzureOpsRegistered_EvenInHybridStyleSetup()
    {
        var tenantConfig = new TenantConfiguration(
            Guid.NewGuid(), "TestTenant",
            new ConfigurationBuilder().Build(),
            Array.Empty<string>());
        _tenantContextAccessor.CurrentTenant.Returns(tenantConfig);

        _azureOps.GenerateSasTokenAsync(
                Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://account.file.core.windows.net/share/file?sv=sas");

        var @event = new FileUploadedDomainEvent(CreateFileWithApplication(), "hash-abc", DateTime.UtcNow);

        await _handler.Handle(@event, CancellationToken.None);

        await _azureOps.Received(1).GenerateSasTokenAsync(
            Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var publishedCalls = _eventPublisher.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEventPublisher.PublishAsync))
            .Select(c => c.GetArguments()[0])
            .OfType<ScanRequestedEvent>()
            .ToList();
        Assert.Single(publishedCalls);
        Assert.True(publishedCalls[0].IsAzureFileShare);
        Assert.StartsWith("https://", publishedCalls[0].FileUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_ShouldUseFileUri_WhenAzureOpsNotRegistered()
    {
        var localOnlyHandler = new FileUploadedDomainEventHandler(
            _logger,
            _eventPublisher,
            _tenantContextAccessor,
            _tenantAzureFactory,
            Array.Empty<IAzureSpecificOperations>(),
            _applicationRepository,
            _userRepository,
            _eventTriggerDispatcher);

        var tenantConfig = new TenantConfiguration(
            Guid.NewGuid(), "TestTenant",
            new ConfigurationBuilder().Build(),
            Array.Empty<string>());
        _tenantContextAccessor.CurrentTenant.Returns(tenantConfig);

        var @event = new FileUploadedDomainEvent(CreateFileWithApplication(), "hash-abc", DateTime.UtcNow);

        await localOnlyHandler.Handle(@event, CancellationToken.None);

        await _azureOps.DidNotReceive()
            .GenerateSasTokenAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var publishedCalls = _eventPublisher.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEventPublisher.PublishAsync))
            .Select(c => c.GetArguments()[0])
            .OfType<ScanRequestedEvent>()
            .ToList();
        Assert.Single(publishedCalls);
        Assert.False(publishedCalls[0].IsAzureFileShare);
        Assert.StartsWith("file:///", publishedCalls[0].FileUri, StringComparison.Ordinal);
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
