using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.FlexForms.Utils.File;
using Microsoft.AspNetCore.Http;
using MockQueryable;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Security.Claims;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Applications;

public class DownloadFileQueryHandlerTests
{
    private static ITenantPermissionFilter CreateTenantPermissionFilter(bool belongsToTenant = true)
    {
        var filter = Substitute.For<ITenantPermissionFilter>();
        filter.ApplicationBelongsToCurrentTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(belongsToTenant);
        return filter;
    }

    private readonly IEaRepository<File> _uploadRepository;
    private readonly IEaRepository<User> _userRepository;
    private readonly ITenantAwareFileStorageService _fileStorageService;
    private readonly IEaRepository<Domain.Entities.Application> _applicationRepository;
    private readonly IPermissionCheckerService _permissionCheckerService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly DownloadFileQueryHandler _handler;

    public DownloadFileQueryHandlerTests()
    {
        _uploadRepository = Substitute.For<IEaRepository<File>>();
        _userRepository = Substitute.For<IEaRepository<User>>();
        _fileStorageService = Substitute.For<ITenantAwareFileStorageService>();
        _applicationRepository = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        _permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

        _handler = new DownloadFileQueryHandler(
            _uploadRepository,
            _userRepository,
            _fileStorageService,
            _applicationRepository,
            _permissionCheckerService,
            _httpContextAccessor,
            CreateTenantPermissionFilter());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Success_When_Valid_Request(
        Domain.Entities.Application application,
        User user,
        File file,
        Stream fileStream,
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

        // Create a file with the matching ID
        var fileWithMatchingId = new File(
            new FileId(fileId), // Use the fileId parameter
            file.ApplicationId,
            file.Name,
            file.Description,
            file.OriginalFileName,
            file.FileName,
            file.Path,
            file.UploadedOn,
            file.UploadedBy, file.FileSize);

        var fileQueryable = new List<File> { fileWithMatchingId }.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(fileQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        _fileStorageService.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(fileStream);

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(fileStream, result.Value.FileStream);
        Assert.Equal(fileWithMatchingId.OriginalFileName, result.Value.FileName);
        Assert.Equal(fileWithMatchingId.OriginalFileName.GetContentType(), result.Value.ContentType);

        await _fileStorageService.Received(1).DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_Not_Authenticated(
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_User_Not_Found(
        Domain.Entities.Application application,
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var queryable = new List<User>().AsQueryable().BuildMock();
        _userRepository.Query().Returns(queryable);

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_Application_Not_Found(
        User user,
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

        var applicationQueryable = new List<Domain.Entities.Application>().AsQueryable().BuildMock();
        _applicationRepository.Query().Returns(applicationQueryable);

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Application not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_User_No_Read_Permission(
        Domain.Entities.Application application,
        User user,
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(false);

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User does not have permission to download this file", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_File_Not_Found(
        Domain.Entities.Application application,
        User user,
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

        var fileQueryable = new List<File>().AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(fileQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("File not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_Exception_Occurs(
        Domain.Entities.Application application,
        User user,
        File file,
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

        // Create a file with the matching ID
        var fileWithMatchingId = new File(
            new FileId(fileId), // Use the fileId parameter
            file.ApplicationId,
            file.Name,
            file.Description,
            file.OriginalFileName,
            file.FileName,
            file.Path,
            file.UploadedOn,
            file.UploadedBy, file.FileSize);

        var fileQueryable = new List<File> { fileWithMatchingId }.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(fileQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        _fileStorageService.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Storage error"));

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Storage error", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Use_Email_When_PrincipalId_Contains_At_Symbol(
        Domain.Entities.Application application,
        User user,
        File file,
        Stream fileStream,
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

        // Create a file with the matching ID
        var fileWithMatchingId = new File(
            new FileId(fileId), // Use the fileId parameter
            file.ApplicationId,
            file.Name,
            file.Description,
            file.OriginalFileName,
            file.FileName,
            file.Path,
            file.UploadedOn,
            file.UploadedBy, file.FileSize);

        var fileQueryable = new List<File> { fileWithMatchingId }.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(fileQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        _fileStorageService.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(fileStream);

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _userRepository.Received(1).Query();
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Use_ExternalProviderId_When_PrincipalId_Does_Not_Contain_At_Symbol(
        Domain.Entities.Application application,
        User user,
        File file,
        Stream fileStream,
        Guid fileId,
        ApplicationId applicationId)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new("azp", "external-provider-id")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

        // Create a file with the matching ID
        var fileWithMatchingId = new File(
            new FileId(fileId), // Use the fileId parameter
            file.ApplicationId,
            file.Name,
            file.Description,
            file.OriginalFileName,
            file.FileName,
            file.Path,
            file.UploadedOn,
            file.UploadedBy, file.FileSize);

        var fileQueryable = new List<File> { fileWithMatchingId }.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(fileQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        _fileStorageService.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(fileStream);

        var query = new DownloadFileQuery(fileId, applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _userRepository.Received(1).Query();
    }
} 