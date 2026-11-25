using System.Reflection;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MockQueryable.NSubstitute;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Applications;

public class GetApplicationFileValidationGateQueryHandlerTests
{
    private static readonly Guid AppGuid = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid TemplateGuid = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly UserId Uploader = new(Guid.NewGuid());

    private readonly IEaRepository<Domain.Entities.Application> _applications = Substitute.For<IEaRepository<Domain.Entities.Application>>();
    private readonly IEaRepository<File> _files = Substitute.For<IEaRepository<File>>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly IFileValidationModeResolver _modes = Substitute.For<IFileValidationModeResolver>();
    private readonly ITenantPermissionFilter _tenantFilter = Substitute.For<ITenantPermissionFilter>();
    private readonly List<File> _storedFiles = [];

    public GetApplicationFileValidationGateQueryHandlerTests()
    {
        var applications = new List<Domain.Entities.Application> { Application() }.BuildMockDbSet();
        _applications.Query().Returns(applications);
        _tenantFilter.ApplicationBelongsToCurrentTenantAsync(AppGuid, Arg.Any<CancellationToken>()).Returns(true);
        _permissions.HasPermission(ResourceType.Application, AppGuid.ToString(), AccessType.Read).Returns(true);
    }

    [Theory]
    [InlineData(FileValidationMode.Off, FileValidationStatus.Pending, true)]
    [InlineData(FileValidationMode.Off, FileValidationStatus.Failed, true)]
    [InlineData(FileValidationMode.Off, FileValidationStatus.Passed, true)]
    [InlineData(FileValidationMode.FailOnInvalid, FileValidationStatus.Pending, true)]
    [InlineData(FileValidationMode.FailOnInvalid, FileValidationStatus.Failed, false)]
    [InlineData(FileValidationMode.FailOnInvalid, FileValidationStatus.Passed, true)]
    [InlineData(FileValidationMode.RequirePassed, FileValidationStatus.Pending, false)]
    [InlineData(FileValidationMode.RequirePassed, FileValidationStatus.Failed, false)]
    [InlineData(FileValidationMode.RequirePassed, FileValidationStatus.Passed, true)]
    [InlineData(FileValidationMode.RequirePassed, FileValidationStatus.NotRequired, true)]
    public async Task Mode_DecidesWhichFilesBlockSubmission(FileValidationMode mode, FileValidationStatus status, bool canSubmit)
    {
        _modes.Resolve(TemplateGuid).Returns(mode);
        var file = StoreFile("evidence.pdf", status);

        var result = await HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(mode, result.Value!.Mode);
        Assert.Equal(canSubmit, result.Value.CanSubmit);
        if (canSubmit)
        {
            Assert.Empty(result.Value.BlockingFiles);
        }
        else
        {
            var blocking = Assert.Single(result.Value.BlockingFiles);
            Assert.Equal(file.Id!.Value, blocking.FileId);
            Assert.Equal("evidence.pdf", blocking.OriginalFileName);
            Assert.Equal(status, blocking.ValidationStatus);
        }
    }

    [Fact]
    public async Task BlockingFiles_CarryTheValidatorsMessage_AndOnlyBlockingFilesAreListed()
    {
        _modes.Resolve(TemplateGuid).Returns(FileValidationMode.FailOnInvalid);
        StoreFile("ok.pdf", FileValidationStatus.Passed);
        StoreFile("bad.pdf", FileValidationStatus.Failed, "Virus found");
        StoreFile("waiting.pdf", FileValidationStatus.Pending);

        var result = await HandleAsync();

        var blocking = Assert.Single(result.Value!.BlockingFiles);
        Assert.Equal("bad.pdf", blocking.OriginalFileName);
        Assert.Equal("Virus found", blocking.ValidationMessage);
    }

    [Fact]
    public async Task NoFiles_CanSubmit()
    {
        _modes.Resolve(TemplateGuid).Returns(FileValidationMode.RequirePassed);

        var result = await HandleAsync();

        Assert.True(result.Value!.CanSubmit);
    }

    [Fact]
    public async Task FileReadPermissionAlone_IsEnough()
    {
        _permissions.HasPermission(ResourceType.Application, AppGuid.ToString(), AccessType.Read).Returns(false);
        _permissions.HasPermission(ResourceType.ApplicationFiles, AppGuid.ToString(), AccessType.Read).Returns(true);

        var result = await HandleAsync();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task NoReadPermission_IsForbidden()
    {
        _permissions.HasPermission(ResourceType.Application, AppGuid.ToString(), AccessType.Read).Returns(false);

        var result = await HandleAsync();

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        _modes.DidNotReceiveWithAnyArgs().Resolve(default);
    }

    [Fact]
    public async Task UnknownApplication_IsNotFound()
    {
        var result = await HandleAsync(Guid.NewGuid(), grantRead: true);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task ApplicationInAnotherTenant_IsNotFound()
    {
        _tenantFilter.ApplicationBelongsToCurrentTenantAsync(AppGuid, Arg.Any<CancellationToken>()).Returns(false);

        var result = await HandleAsync();

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        _modes.DidNotReceiveWithAnyArgs().Resolve(default);
    }

    private Task<Result<FileValidationGateDto>> HandleAsync(
        Guid? applicationId = null,
        bool grantRead = false)
    {
        var id = applicationId ?? AppGuid;
        if (grantRead)
            _permissions.HasPermission(ResourceType.Application, id.ToString(), AccessType.Read).Returns(true);

        var files = _storedFiles.BuildMockDbSet();
        _files.Query().Returns(files);
        var handler = new GetApplicationFileValidationGateQueryHandler(
            _applications, _files, _permissions, _modes, new ApplicationFileValidationPolicy(), _tenantFilter);
        return handler.Handle(new GetApplicationFileValidationGateQuery(id), CancellationToken.None);
    }

    private File StoreFile(string originalName, FileValidationStatus status, string? message = null)
    {
        var file = new File(
            new FileId(Guid.NewGuid()), new ApplicationId(AppGuid), originalName, null, originalName,
            $"{Guid.NewGuid()}.pdf", "/files", DateTime.UtcNow, Uploader, 100);
        if (status != FileValidationStatus.NotRequired)
            file.RequireExternalValidation();
        if (status is FileValidationStatus.Passed or FileValidationStatus.Failed)
            file.RecordValidationResult(status == FileValidationStatus.Passed, message, DateTime.UtcNow, "scanner");
        _storedFiles.Add(file);
        return file;
    }

    private static Domain.Entities.Application Application()
    {
        var versionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(new ApplicationId(AppGuid), "APP-1", versionId, DateTime.UtcNow, Uploader);
        var version = new TemplateVersion(versionId, new TemplateId(TemplateGuid), "1.0", "{}", DateTime.UtcNow, Uploader);
        typeof(Domain.Entities.Application)
            .GetProperty(nameof(Domain.Entities.Application.TemplateVersion), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(application, version);
        return application;
    }
}
