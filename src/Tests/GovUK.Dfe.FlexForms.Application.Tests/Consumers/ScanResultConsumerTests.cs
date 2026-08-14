using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Consumers;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using MockQueryable;
using NSubstitute;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Application.Tests.Consumers;

public class ScanResultConsumerTests
{
    private readonly ILogger<ScanResultConsumer> _logger;
    private readonly IEaRepository<File> _fileRepository;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ISender _sender;
    private readonly ScanResultConsumer _consumer;

    public ScanResultConsumerTests()
    {
        _logger = Substitute.For<ILogger<ScanResultConsumer>>();
        _fileRepository = Substitute.For<IEaRepository<File>>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        _sender = Substitute.For<ISender>();

        var tenant = new TenantConfiguration(
            Guid.NewGuid(),
            "TestTenant",
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Array.Empty<string>());
        _tenantContextAccessor.CurrentTenant.Returns(tenant);

        _consumer = new ScanResultConsumer(
            _logger, _fileRepository, _tenantContextAccessor, _sender);
    }

    private ConsumeContext<ScanResultEvent> CreateConsumeContext(ScanResultEvent message, Guid? tenantId = null)
    {
        var context = Substitute.For<ConsumeContext<ScanResultEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        var headers = Substitute.For<Headers>();
        if (tenantId.HasValue)
        {
            headers.Get<string>("TenantId").Returns(tenantId.Value.ToString());
        }
        else
        {
            headers.Get<string>("TenantId").Returns((string?)null);
        }
        context.Headers.Returns(headers);

        return context;
    }

    [Fact]
    public async Task Consume_ShouldSkip_WhenFileNameIsEmpty()
    {
        var scanResult = new ScanResultEvent(
            ServiceName: "test-service",
            FileUri: "some/path/",
            FileName: "",
            FileId: Guid.NewGuid().ToString(),
            Path: "some/path",
            Status: ScanStatus.Completed,
            Outcome: VirusScanOutcome.Clean);

        var context = CreateConsumeContext(scanResult);

        await _consumer.Consume(context);

        _fileRepository.DidNotReceive().Query();
    }

    [Fact]
    public async Task Consume_ShouldSkip_WhenPathIsEmpty()
    {
        var scanResult = new ScanResultEvent(
            ServiceName: "test-service",
            FileUri: "test.pdf",
            FileName: "test.pdf",
            FileId: Guid.NewGuid().ToString(),
            Path: "",
            Status: ScanStatus.Completed,
            Outcome: VirusScanOutcome.Clean);

        var context = CreateConsumeContext(scanResult);

        await _consumer.Consume(context);

        _fileRepository.DidNotReceive().Query();
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization), typeof(UserCustomization))]
    public async Task Consume_ShouldLogInformation_WhenFileIsClean(
        File file, User user, long fileSize)
    {
        var fileId = new FileId(Guid.NewGuid());
        var fileWithId = new File(
            fileId, file.ApplicationId, file.Name, file.Description,
            file.OriginalFileName, file.FileName, file.Path,
            file.UploadedOn, file.UploadedBy, fileSize);

        var userProp = typeof(File).GetProperty("UploadedByUser");
        userProp?.SetValue(fileWithId, user);

        var fileQueryable = new List<File> { fileWithId }.AsQueryable().BuildMock();
        _fileRepository.Query().Returns(fileQueryable);

        var scanResult = new ScanResultEvent(
            ServiceName: "test-service",
            FileUri: $"{fileWithId.Path}/{fileWithId.FileName}",
            FileName: fileWithId.FileName,
            FileId: fileId.Value.ToString(),
            Path: fileWithId.Path,
            Status: ScanStatus.Completed,
            Outcome: VirusScanOutcome.Clean);

        var context = CreateConsumeContext(scanResult);

        await _consumer.Consume(context);

        await _sender.DidNotReceive().Send(
            Arg.Any<DeleteInfectedFileCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization), typeof(UserCustomization))]
    public async Task Consume_ShouldSendDeleteCommand_WhenFileIsInfected(
        File file, User user, long fileSize)
    {
        var fileId = new FileId(Guid.NewGuid());
        var fileWithId = new File(
            fileId, file.ApplicationId, file.Name, file.Description,
            file.OriginalFileName, file.FileName, file.Path,
            file.UploadedOn, file.UploadedBy, fileSize);

        var userProp = typeof(File).GetProperty("UploadedByUser");
        userProp?.SetValue(fileWithId, user);

        var fileQueryable = new List<File> { fileWithId }.AsQueryable().BuildMock();
        _fileRepository.Query().Returns(fileQueryable);

        _sender.Send(Arg.Any<DeleteInfectedFileCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var scanResult = new ScanResultEvent(
            ServiceName: "test-service",
            FileUri: $"{fileWithId.Path}/{fileWithId.FileName}",
            FileName: fileWithId.FileName,
            FileId: fileId.Value.ToString(),
            Path: fileWithId.Path,
            Status: ScanStatus.Completed,
            Outcome: VirusScanOutcome.Infected,
            MalwareName: "TestMalware");

        var context = CreateConsumeContext(scanResult);

        await _consumer.Consume(context);

        await _sender.Received(1).Send(
            Arg.Is<DeleteInfectedFileCommand>(cmd => cmd.FileId == fileId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_ShouldLogWarning_WhenFileNotFoundInDatabase()
    {
        var fileQueryable = new List<File>().AsQueryable().BuildMock();
        _fileRepository.Query().Returns(fileQueryable);

        var scanResult = new ScanResultEvent(
            ServiceName: "test-service",
            FileUri: "some/path/notfound.pdf",
            FileName: "notfound.pdf",
            FileId: Guid.NewGuid().ToString(),
            Path: "some/path",
            Status: ScanStatus.Completed,
            Outcome: VirusScanOutcome.Clean);

        var context = CreateConsumeContext(scanResult);

        await _consumer.Consume(context);

        await _sender.DidNotReceive().Send(
            Arg.Any<DeleteInfectedFileCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_ShouldSkip_WhenTenantContextMissing()
    {
        _tenantContextAccessor.CurrentTenant.Returns((TenantConfiguration?)null);

        var scanResult = new ScanResultEvent(
            ServiceName: "test-service",
            FileUri: "some/path/file.pdf",
            FileName: "file.pdf",
            FileId: Guid.NewGuid().ToString(),
            Path: "some/path",
            Outcome: VirusScanOutcome.Infected,
            MalwareName: "TestMalware");

        await _consumer.Consume(CreateConsumeContext(scanResult));

        _fileRepository.DidNotReceive().Query();
        await _sender.DidNotReceive().Send(
            Arg.Any<DeleteInfectedFileCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization), typeof(UserCustomization))]
    public async Task Consume_ShouldSkip_WhenUserDoesNotMatchUploader(
        File file, User user, long fileSize)
    {
        var fileId = new FileId(Guid.NewGuid());
        var fileWithId = new File(
            fileId, file.ApplicationId, file.Name, file.Description,
            file.OriginalFileName, file.FileName, file.Path,
            file.UploadedOn, file.UploadedBy, fileSize);

        var userProp = typeof(File).GetProperty("UploadedByUser");
        userProp?.SetValue(fileWithId, user);

        _fileRepository.Query().Returns(new List<File> { fileWithId }.AsQueryable().BuildMock());

        var scanResult = new ScanResultEvent(
            ServiceName: "test-service",
            FileUri: $"{fileWithId.Path}/{fileWithId.FileName}",
            FileName: fileWithId.FileName,
            FileId: fileId.Value.ToString(),
            Path: fileWithId.Path,
            Outcome: VirusScanOutcome.Infected,
            MalwareName: "TestMalware",
            Metadata: new Dictionary<string, object>
            {
                ["userId"] = Guid.NewGuid()
            });

        await _consumer.Consume(CreateConsumeContext(scanResult));

        await _sender.DidNotReceive().Send(
            Arg.Any<DeleteInfectedFileCommand>(), Arg.Any<CancellationToken>());
    }
}
