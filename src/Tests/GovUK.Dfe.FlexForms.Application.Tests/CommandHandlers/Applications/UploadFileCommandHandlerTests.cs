using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.FlexForms.Utils.File;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Security.Claims;
using MockQueryable;
using NSubstitute.ExceptionExtensions;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class UploadFileCommandHandlerTests
{
    private readonly IEaRepository<File> _uploadRepository;
    private readonly IEaRepository<Domain.Entities.Application> _applicationRepository;
    private readonly IEaRepository<User> _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantAwareFileStorageService _fileStorageService;
    private readonly IFileFactory _fileFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPermissionCheckerService _permissionCheckerService;
    private readonly IFileValidationModeResolver _fileValidationModeResolver;
    private readonly UploadFileCommandHandler _handler;

    public UploadFileCommandHandlerTests()
    {
        _uploadRepository = Substitute.For<IEaRepository<File>>();
        _applicationRepository = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        _userRepository = Substitute.For<IEaRepository<User>>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _fileStorageService = Substitute.For<ITenantAwareFileStorageService>();
        _fileFactory = Substitute.For<IFileFactory>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        _fileValidationModeResolver = Substitute.For<IFileValidationModeResolver>();
        _fileValidationModeResolver.Resolve(Arg.Any<Guid?>()).Returns(FileValidationMode.Off);
        _fileValidationModeResolver.IsExtensionSubjectToValidation(Arg.Any<string?>()).Returns(true);

        var tenantPermissionFilter = Substitute.For<ITenantPermissionFilter>();
        tenantPermissionFilter.ApplicationBelongsToCurrentTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _handler = new UploadFileCommandHandler(
            _uploadRepository,
            _applicationRepository,
            _userRepository,
            _unitOfWork,
            _fileStorageService,
            _fileFactory,
            _httpContextAccessor,
            _permissionCheckerService,
            _fileValidationModeResolver,
            tenantPermissionFilter);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Success_When_Valid_Request(
        Domain.Entities.Application application,
        User user,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent,
        File uploadedFile)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        // Create an authenticated identity - passing authentication type makes it authenticated
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Create a user with the same email as in the HTTP context
        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com", // Match the email in the HTTP context
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var queryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(queryable);

        // Create an application with the same ID as the applicationId parameter
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId, // Use the same ID as the parameter
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var uploadQueryable = new List<File>().AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Write)
            .Returns(true);

        // Mock file storage service
        _fileStorageService.UploadAsync(
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _fileFactory.CreateUpload(
            Arg.Any<FileId>(),
            Arg.Any<Domain.Entities.Application>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<UserId>(),
            Arg.Any<long>(),
            Arg.Any<string>())
            .Returns(uploadedFile);

        // Create a seekable MemoryStream that supports Length and can be read multiple times
        // The handler reads the stream for upload, then accesses Length and computes hash
        Stream testStream;
        if (fileContent is MemoryStream ms && ms.CanSeek)
        {
            testStream = ms;
            testStream.Position = 0;
        }
        else
        {
            // Create a new MemoryStream with test data
            var testData = System.Text.Encoding.UTF8.GetBytes("test file content");
            testStream = new MemoryStream(testData);
        }

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, testStream);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        if (!result.IsSuccess)
        {
            var errorMsg = result.Error ?? "Unknown error";
            Assert.True(false, $"Result failed: - {errorMsg}");
        }
        Assert.NotNull(result.Value);
        Assert.Equal(uploadedFile.Id!.Value, result.Value.Id);
        Assert.Equal(uploadedFile.ApplicationId.Value, result.Value.ApplicationId);
        Assert.Equal(uploadedFile.UploadedBy.Value, result.Value.UploadedBy);
        Assert.Equal(uploadedFile.Name, result.Value.Name);
        Assert.Equal(uploadedFile.Description, result.Value.Description);
        Assert.Equal(uploadedFile.OriginalFileName, result.Value.OriginalFileName);
        Assert.Equal(uploadedFile.FileName, result.Value.FileName);
        Assert.Equal(uploadedFile.UploadedOn, result.Value.UploadedOn);

        await _uploadRepository.Received(1).AddAsync(uploadedFile, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _fileStorageService.Received(1).UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_Not_Authenticated(
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent)
    {
        // Arrange
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, fileContent);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_User_Not_Found(
        Domain.Entities.Application application,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        // Create an authenticated identity - passing authentication type makes it authenticated
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var queryable = new List<User>().AsQueryable().BuildMock();
        _userRepository.Query().Returns(queryable);

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, fileContent);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_Application_Not_Found(
        User user,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        // Create an authenticated identity - passing authentication type makes it authenticated
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Create a user with the same email as in the HTTP context
        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com", // Match the email in the HTTP context
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var userQueryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(userQueryable);

        // Application does NOT exist
        var applicationQueryable = new List<Domain.Entities.Application>().AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, fileContent);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Application not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_User_No_Permission(
        Domain.Entities.Application application,
        User user,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        // Create an authenticated identity - passing authentication type makes it authenticated
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Create a user with the same email as in the HTTP context
        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com", // Match the email in the HTTP context
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var queryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(queryable);

        // Application with matching ID
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Write)
            .Returns(false);

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, fileContent);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User does not have permission to upload files for this application", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_File_Already_Exists_And_Is_Referenced_In_Response(
        Domain.Entities.Application application,
        User user,
        File existingFile,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        // Create an authenticated identity - passing authentication type makes it authenticated
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Create a user with the same email as in the HTTP context
        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com", // Match the email in the HTTP context
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var queryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(queryable);

        // Create a file with the correct ApplicationId and FileName that the handler will look for
        var hashedFileName = FileNameHasher.HashFileName(originalFileName);
        var existingFileWithMatchingProperties = new File(
            existingFile.Id!,
            applicationId, // Use the applicationId parameter
            existingFile.Name,
            existingFile.Description,
            existingFile.OriginalFileName,
            hashedFileName, // Use the hashed filename that the handler generates
            existingFile.Path,
            existingFile.UploadedOn,
            existingFile.UploadedBy,
            existingFile.FileSize);

        // Application with matching ID and a response that CONTAINS the file ID (not orphaned)
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        // Add a response that references the existing file ID
        var responseBody = $"{{\"fileUpload\": \"{existingFileWithMatchingProperties.Id!.Value}\"}}";
        var response = new ApplicationResponse(
            new ResponseId(Guid.NewGuid()),
            applicationId,
            responseBody,
            DateTime.UtcNow,
            user.Id!);
        applicationWithMatchingId.AddResponse(response);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var uploadQueryable = new List<File> { existingFileWithMatchingProperties }.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Write)
            .Returns(true);

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, fileContent);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("The selected file has already been uploaded. Upload a file with a different name.", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Delete_Orphaned_File_And_Upload_When_No_Response_Exists(
        Domain.Entities.Application application,
        User user,
        File existingFile,
        File uploadedFile,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com",
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var userQueryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(userQueryable);

        // Create existing file with matching properties
        var hashedFileName = FileNameHasher.HashFileName(originalFileName);
        var existingFileWithMatchingProperties = new File(
            existingFile.Id!,
            applicationId,
            existingFile.Name,
            existingFile.Description,
            existingFile.OriginalFileName,
            hashedFileName,
            existingFile.Path,
            existingFile.UploadedOn,
            existingFile.UploadedBy,
            existingFile.FileSize);

        // Application with NO responses (file is orphaned)
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var uploadQueryable = new List<File> { existingFileWithMatchingProperties }.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Write)
            .Returns(true);

        _fileStorageService.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _fileStorageService.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _fileFactory.CreateUpload(
            Arg.Any<FileId>(),
            Arg.Any<Domain.Entities.Application>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<UserId>(),
            Arg.Any<long>(),
            Arg.Any<string>())
            .Returns(uploadedFile);

        var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test file content"));
        var command = new UploadFileCommand(applicationId, name, description, originalFileName, testStream);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);

        // Verify orphaned file was deleted
        _fileFactory.Received(1).DeleteFile(existingFileWithMatchingProperties);
        await _uploadRepository.Received(1).RemoveAsync(existingFileWithMatchingProperties, Arg.Any<CancellationToken>());
        await _fileStorageService.Received(1).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Verify new file was uploaded
        await _uploadRepository.Received(1).AddAsync(uploadedFile, Arg.Any<CancellationToken>());
        await _fileStorageService.Received(1).UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Delete_Orphaned_File_And_Upload_When_File_Not_In_Response(
        Domain.Entities.Application application,
        User user,
        File existingFile,
        File uploadedFile,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com",
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var userQueryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(userQueryable);

        // Create existing file with matching properties
        var hashedFileName = FileNameHasher.HashFileName(originalFileName);
        var existingFileWithMatchingProperties = new File(
            existingFile.Id!,
            applicationId,
            existingFile.Name,
            existingFile.Description,
            existingFile.OriginalFileName,
            hashedFileName,
            existingFile.Path,
            existingFile.UploadedOn,
            existingFile.UploadedBy,
            existingFile.FileSize);

        // Application with a response that does NOT contain the file ID (orphaned)
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        // Add a response that does NOT reference the existing file ID
        var responseBody = "{\"someOtherField\": \"someValue\", \"differentFileId\": \"00000000-0000-0000-0000-000000000000\"}";
        var response = new ApplicationResponse(
            new ResponseId(Guid.NewGuid()),
            applicationId,
            responseBody,
            DateTime.UtcNow,
            user.Id!);
        applicationWithMatchingId.AddResponse(response);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var uploadQueryable = new List<File> { existingFileWithMatchingProperties }.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Write)
            .Returns(true);

        _fileStorageService.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _fileStorageService.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _fileFactory.CreateUpload(
            Arg.Any<FileId>(),
            Arg.Any<Domain.Entities.Application>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<UserId>(),
            Arg.Any<long>(),
            Arg.Any<string>())
            .Returns(uploadedFile);

        var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test file content"));
        var command = new UploadFileCommand(applicationId, name, description, originalFileName, testStream);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);

        // Verify orphaned file was deleted
        _fileFactory.Received(1).DeleteFile(existingFileWithMatchingProperties);
        await _uploadRepository.Received(1).RemoveAsync(existingFileWithMatchingProperties, Arg.Any<CancellationToken>());

        // Verify new file was uploaded
        await _uploadRepository.Received(1).AddAsync(uploadedFile, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Continue_Upload_When_Orphaned_File_Storage_Delete_Fails(
        Domain.Entities.Application application,
        User user,
        File existingFile,
        File uploadedFile,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com",
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var userQueryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(userQueryable);

        // Create existing file with matching properties
        var hashedFileName = FileNameHasher.HashFileName(originalFileName);
        var existingFileWithMatchingProperties = new File(
            existingFile.Id!,
            applicationId,
            existingFile.Name,
            existingFile.Description,
            existingFile.OriginalFileName,
            hashedFileName,
            existingFile.Path,
            existingFile.UploadedOn,
            existingFile.UploadedBy,
            existingFile.FileSize);

        // Application with NO responses (file is orphaned)
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var uploadQueryable = new List<File> { existingFileWithMatchingProperties }.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Write)
            .Returns(true);

        // Simulate storage delete failure for orphaned file
        _fileStorageService.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("Storage unavailable"));

        _fileStorageService.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _fileFactory.CreateUpload(
            Arg.Any<FileId>(),
            Arg.Any<Domain.Entities.Application>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<UserId>(),
            Arg.Any<long>(),
            Arg.Any<string>())
            .Returns(uploadedFile);

        var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test file content"));
        var command = new UploadFileCommand(applicationId, name, description, originalFileName, testStream);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert - should still succeed even though storage delete failed
        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);

        // Verify orphaned file was still deleted from database (even if storage failed)
        _fileFactory.Received(1).DeleteFile(existingFileWithMatchingProperties);
        await _uploadRepository.Received(1).RemoveAsync(existingFileWithMatchingProperties, Arg.Any<CancellationToken>());

        // Verify new file was still uploaded
        await _uploadRepository.Received(1).AddAsync(uploadedFile, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_Exception_Occurs(
        Domain.Entities.Application application,
        User user,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        // Create an authenticated identity - passing authentication type makes it authenticated
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Create a user with the same email as in the HTTP context
        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com", // Match the email in the HTTP context
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var userQueryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(userQueryable);

        // Application with matching ID
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        // Simulate exception in file storage
        _fileStorageService
            .When(x => x.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(x => throw new Exception("Storage error"));

        // Allow user to have permission to upload files
        _permissionCheckerService.HasPermission(
            ResourceType.ApplicationFiles,
            applicationWithMatchingId.Id!.Value.ToString(),
            AccessType.Write
        ).Returns(true);

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, fileContent);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Storage error", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Use_Email_When_PrincipalId_Contains_At_Symbol(
        Domain.Entities.Application application,
        User user,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent,
        File uploadedFile)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        // Create an authenticated identity - passing authentication type makes it authenticated
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Create a user with the same email as in the HTTP context
        var userWithMatchingEmail = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "test@example.com", // Match the email in the HTTP context
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            user.ExternalProviderId,
            user.Permissions);

        var queryable = new List<User> { userWithMatchingEmail }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(queryable);

        // Application with matching ID
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var uploadQueryable = new List<File>().AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Write)
            .Returns(true);

        // Mock file storage service
        _fileStorageService.UploadAsync(
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _fileFactory.CreateUpload(
            Arg.Any<FileId>(),
            Arg.Any<Domain.Entities.Application>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<UserId>(),
            Arg.Any<long>(),
            Arg.Any<string>())
            .Returns(uploadedFile);

        // Create a seekable MemoryStream that supports Length and can be read multiple times
        Stream testStream;
        if (fileContent is MemoryStream ms && ms.CanSeek)
        {
            testStream = ms;
            testStream.Position = 0;
        }
        else
        {
            // Create a new MemoryStream with test data
            var testData = System.Text.Encoding.UTF8.GetBytes("test file content");
            testStream = new MemoryStream(testData);
        }

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, testStream);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        if (!result.IsSuccess)
        {
            var errorMsg = result.Error ?? "Unknown error";
            Assert.True(false, $"Result failed: - {errorMsg}");
        }
        _userRepository.Received(1).Query();
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Use_ExternalProviderId_When_PrincipalId_Does_Not_Contain_At_Symbol(
        Domain.Entities.Application application,
        User user,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        Stream fileContent,
        File uploadedFile)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new("azp", "external-provider-id")
        };
        // Create an authenticated identity - passing authentication type makes it authenticated
        var identity = new ClaimsIdentity(claims, "Bearer", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = new ClaimsPrincipal(identity);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Create a user with the same external provider ID as in the HTTP context
        var userWithMatchingExternalProviderId = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            user.Email,
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            "external-provider-id", // Match the external provider ID in the HTTP context
            user.Permissions);

        var queryable = new List<User> { userWithMatchingExternalProviderId }.AsQueryable().BuildMock();
        _userRepository.Query().Returns(queryable);

        // Application with matching ID
        var applicationWithMatchingId = new Domain.Entities.Application(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersionId,
            application.CreatedOn,
            application.CreatedBy,
            application.Status,
            application.LastModifiedOn,
            application.LastModifiedBy);

        var applicationQueryable = new List<Domain.Entities.Application> { applicationWithMatchingId }.AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var uploadQueryable = new List<File>().AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Write)
            .Returns(true);

        // Mock file storage service
        _fileStorageService.UploadAsync(
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _fileFactory.CreateUpload(
            Arg.Any<FileId>(),
            Arg.Any<Domain.Entities.Application>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<UserId>(),
            Arg.Any<long>(),
            Arg.Any<string>())
            .Returns(uploadedFile);

        // Create a seekable MemoryStream that supports Length and can be read multiple times
        Stream testStream;
        if (fileContent is MemoryStream ms && ms.CanSeek)
        {
            testStream = ms;
            testStream.Position = 0;
        }
        else
        {
            // Create a new MemoryStream with test data
            var testData = System.Text.Encoding.UTF8.GetBytes("test file content");
            testStream = new MemoryStream(testData);
        }

        var command = new UploadFileCommand(applicationId, name, description, originalFileName, testStream);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        if (!result.IsSuccess)
        {
            var errorMsg = result.Error ?? "Unknown error";
            Assert.True(false, $"Result failed: - {errorMsg}");
        }
        _userRepository.Received(1).Query();
    }
} 