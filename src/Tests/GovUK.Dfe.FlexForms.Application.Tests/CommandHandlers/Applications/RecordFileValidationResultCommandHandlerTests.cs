using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.NSubstitute;
using NSubstitute;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;
using ApplicationEntity = GovUK.Dfe.FlexForms.Domain.Entities.Application;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class RecordFileValidationResultCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldRecordFailedResult_WhenFileIsPending()
    {
        var file = CreatePendingFile(out var application);
        var fileRepo = Substitute.For<IEaRepository<File>>();
        var files = new[] { file }.AsQueryable().BuildMockDbSet();
        fileRepo.Query().Returns(files);

        var permissions = Substitute.For<IPermissionCheckerService>();
        permissions.CanWriteFileValidation(application.TemplateVersion!.TemplateId.Value.ToString()).Returns(true);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var handler = new RecordFileValidationResultCommandHandler(
            fileRepo,
            permissions,
            unitOfWork,
            NullLogger<RecordFileValidationResultCommandHandler>.Instance);

        var result = await handler.Handle(
            new RecordFileValidationResultCommand(file.Id!.Value, false, "Missing column", "corr-1", "excel-fn"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileValidationStatus.Failed, result.Value!.ValidationStatus);
        Assert.Equal("Missing column", result.Value.ValidationMessage);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenFileMissing()
    {
        var fileRepo = Substitute.For<IEaRepository<File>>();
        var files = Array.Empty<File>().AsQueryable().BuildMockDbSet();
        fileRepo.Query().Returns(files);

        var handler = new RecordFileValidationResultCommandHandler(
            fileRepo,
            Substitute.For<IPermissionCheckerService>(),
            Substitute.For<IUnitOfWork>(),
            NullLogger<RecordFileValidationResultCommandHandler>.Instance);

        var result = await handler.Handle(
            new RecordFileValidationResultCommand(Guid.NewGuid(), true, null, null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("File not found", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerLacksGrant()
    {
        var file = CreatePendingFile(out _);
        var fileRepo = Substitute.For<IEaRepository<File>>();
        var files = new[] { file }.AsQueryable().BuildMockDbSet();
        fileRepo.Query().Returns(files);

        var permissions = Substitute.For<IPermissionCheckerService>();
        permissions.CanWriteFileValidation(Arg.Any<string>()).Returns(false);

        var handler = new RecordFileValidationResultCommandHandler(
            fileRepo,
            permissions,
            Substitute.For<IUnitOfWork>(),
            NullLogger<RecordFileValidationResultCommandHandler>.Instance);

        var result = await handler.Handle(
            new RecordFileValidationResultCommand(file.Id!.Value, true, null, null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ShouldConflict_WhenApplicationSubmitted()
    {
        var file = CreatePendingFile(out var application, ApplicationStatus.Submitted);
        var fileRepo = Substitute.For<IEaRepository<File>>();
        var files = new[] { file }.AsQueryable().BuildMockDbSet();
        fileRepo.Query().Returns(files);

        var permissions = Substitute.For<IPermissionCheckerService>();
        permissions.CanWriteFileValidation(Arg.Any<string>()).Returns(true);

        var handler = new RecordFileValidationResultCommandHandler(
            fileRepo,
            permissions,
            Substitute.For<IUnitOfWork>(),
            NullLogger<RecordFileValidationResultCommandHandler>.Instance);

        var result = await handler.Handle(
            new RecordFileValidationResultCommand(file.Id!.Value, true, null, null, null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Conflict, result.ErrorCode);
    }

    private static File CreatePendingFile(out ApplicationEntity application, ApplicationStatus status = ApplicationStatus.InProgress)
    {
        var userId = new UserId(Guid.NewGuid());
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());
        application = new ApplicationEntity(
            applicationId,
            "APP-1",
            templateVersionId,
            DateTime.UtcNow,
            userId,
            status);

        var templateVersion = new TemplateVersion(
            templateVersionId,
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            userId);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var file = new File(
            new FileId(Guid.NewGuid()),
            applicationId,
            "budget",
            null,
            "budget.xlsx",
            "hashed.xlsx",
            "APP-1",
            DateTime.UtcNow,
            userId,
            12);
        file.SetApplication(application);
        file.RequireExternalValidation();
        return file;
    }
}
