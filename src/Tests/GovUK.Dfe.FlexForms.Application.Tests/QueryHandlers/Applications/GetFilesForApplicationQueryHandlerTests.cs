using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Security.Claims;
using MockQueryable;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Applications;

public class GetFilesForApplicationQueryHandlerTests
{
    private static ITenantPermissionFilter CreateTenantPermissionFilter(bool belongsToTenant = true)
    {
        var filter = Substitute.For<ITenantPermissionFilter>();
        filter.ApplicationBelongsToCurrentTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(belongsToTenant);
        return filter;
    }

    private readonly IEaRepository<File> _uploadRepository;
    private readonly IEaRepository<Domain.Entities.Application> _applicationRepository;
    private readonly IEaRepository<User> _userRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPermissionCheckerService _permissionCheckerService;
    private readonly GetFilesForApplicationQueryHandler _handler;

    public GetFilesForApplicationQueryHandlerTests()
    {
        _uploadRepository = Substitute.For<IEaRepository<File>>();
        _applicationRepository = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        _userRepository = Substitute.For<IEaRepository<User>>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _permissionCheckerService = Substitute.For<IPermissionCheckerService>();

        _handler = new GetFilesForApplicationQueryHandler(
            _uploadRepository,
            _applicationRepository,
            _userRepository,
            _httpContextAccessor,
            _permissionCheckerService,
            CreateTenantPermissionFilter());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Success_When_Valid_Request(
        Domain.Entities.Application application,
        User user,
        List<File> files,
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

        // Ensure all files have the correct ApplicationId
        var filesWithMatchingApplicationId = files.Select(file => new File(
            file.Id!,
            applicationId, // Use the applicationId parameter
            file.Name,
            file.Description,
            file.OriginalFileName,
            file.FileName,
            file.Path,
            file.UploadedOn,
            file.UploadedBy,
            file.FileSize)).ToList();

        var uploadQueryable = filesWithMatchingApplicationId.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        var query = new GetFilesForApplicationQuery(applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(files.Count, result.Value.Count);

        foreach (var file in files)
        {
            var dto = result.Value.FirstOrDefault(f => f.Id == file.Id!.Value);
            Assert.NotNull(dto);
            Assert.Equal(applicationId.Value, dto.ApplicationId); // Use applicationId parameter instead of file.ApplicationId.Value
            Assert.Equal(file.UploadedBy.Value, dto.UploadedBy);
            Assert.Equal(file.Name, dto.Name);
            Assert.Equal(file.Description, dto.Description);
            Assert.Equal(file.OriginalFileName, dto.OriginalFileName);
            Assert.Equal(file.FileName, dto.FileName);
            Assert.Equal(file.UploadedOn, dto.UploadedOn);
        }
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Empty_List_When_No_Files_Found(
        Domain.Entities.Application application,
        User user,
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

        var uploadQueryable = new List<File>().AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        var query = new GetFilesForApplicationQuery(applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Return_Failure_When_Not_Authenticated(
        ApplicationId applicationId)
    {
        // Arrange
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var query = new GetFilesForApplicationQuery(applicationId);

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

        var query = new GetFilesForApplicationQuery(applicationId);

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

        var query = new GetFilesForApplicationQuery(applicationId);

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

        var query = new GetFilesForApplicationQuery(applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User does not have permission to list files for this application", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_Use_Email_When_PrincipalId_Contains_At_Symbol(
        Domain.Entities.Application application,
        User user,
        List<File> files,
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

        var uploadQueryable = files.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        var query = new GetFilesForApplicationQuery(applicationId);

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
        List<File> files,
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

        var uploadQueryable = files.AsQueryable().BuildMock();
        _uploadRepository.Query().Returns(uploadQueryable);

        _permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, applicationWithMatchingId.Id!.Value.ToString(), AccessType.Read)
            .Returns(true);

        var query = new GetFilesForApplicationQuery(applicationId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _userRepository.Received(1).Query();
    }
} 