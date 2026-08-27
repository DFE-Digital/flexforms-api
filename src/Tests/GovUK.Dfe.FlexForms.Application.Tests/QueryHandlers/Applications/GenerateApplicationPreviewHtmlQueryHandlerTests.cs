using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;
using System.Security.Claims;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Applications;

public class GenerateApplicationPreviewHtmlQueryHandlerTests
{
    private static ITenantPermissionFilter CreateTenantPermissionFilter(bool belongsToTenant = true)
    {
        var filter = Substitute.For<ITenantPermissionFilter>();
        filter.ApplicationBelongsToCurrentTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(belongsToTenant);
        return filter;
    }

    private readonly IEaRepository<Domain.Entities.Application> _applicationRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPermissionCheckerService _permissionCheckerService;
    private readonly IStaticHtmlGeneratorService _htmlGeneratorService;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly GenerateApplicationPreviewHtmlQueryHandler _handler;

    public GenerateApplicationPreviewHtmlQueryHandlerTests()
    {
        _applicationRepo = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        _htmlGeneratorService = Substitute.For<IStaticHtmlGeneratorService>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        _handler = new GenerateApplicationPreviewHtmlQueryHandler(
            _applicationRepo,
            _httpContextAccessor,
            _permissionCheckerService,
            _htmlGeneratorService,
            _tenantContextAccessor,
            CreateTenantPermissionFilter());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_ReturnSuccess_When_ValidRequest(
        GenerateApplicationPreviewHtmlQuery query,
        User user)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var applicationId = new ApplicationId(Guid.NewGuid());
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());

        var template = new Template(
            new TemplateId(Guid.NewGuid()),
            "Test Template",
            DateTime.UtcNow,
            user.Id!);

        var templateVersion = new TemplateVersion(
            templateVersionId,
            template.Id!,
            "1.0",
            "{}",
            DateTime.UtcNow,
            user.Id!);
        templateVersion.GetType().GetProperty("Template")?.SetValue(templateVersion, template);

        var application = new Domain.Entities.Application(
            applicationId,
            query.ApplicationReference,
            templateVersionId,
            DateTime.UtcNow,
            user.Id!);

        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        _applicationRepo.Query().Returns(applications);

        _permissionCheckerService.HasPermission(
            ResourceType.Application,
            applicationId.Value.ToString(),
            AccessType.Read)
            .Returns(true);

        SetupTenantContext("https://test.com", ".content", "test-service@test.com", "test-key");

        var htmlContent = "<html><body>Test</body></html>";
        _htmlGeneratorService.GenerateStaticHtmlAsync(
            Arg.Any<string>(),
            Arg.Any<IDictionary<string, string>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(htmlContent);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("text/html", result.Value.ContentType);
        Assert.Contains(query.ApplicationReference, result.Value.FileName);
        Assert.NotNull(result.Value.FileStream);

        await _htmlGeneratorService.Received(1).GenerateStaticHtmlAsync(
            Arg.Is<string>(url => url.Contains(query.ApplicationReference)),
            Arg.Is<IDictionary<string, string>>(headers =>
                headers.ContainsKey("x-service-email") &&
                headers.ContainsKey("x-service-api-key")),
            ".content",
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_Should_ReturnUnauthorized_When_UserNotAuthenticated(
        GenerateApplicationPreviewHtmlQuery query)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // Not authenticated
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_Should_ReturnNotFound_When_ApplicationDoesNotExist(
        GenerateApplicationPreviewHtmlQuery query)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var applications = Array.Empty<Domain.Entities.Application>().AsQueryable().BuildMockDbSet();
        _applicationRepo.Query().Returns(applications);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Application not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_ReturnForbidden_When_UserHasNoPermission(
        GenerateApplicationPreviewHtmlQuery query,
        User user)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var applicationId = new ApplicationId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            query.ApplicationReference,
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        _applicationRepo.Query().Returns(applications);

        // User has no permission
        _permissionCheckerService.HasPermission(
            ResourceType.Application,
            applicationId.Value.ToString(),
            AccessType.Read)
            .Returns(false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User does not have permission to access this application", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_ReturnFailure_When_FrontendBaseUrlNotConfigured(
        GenerateApplicationPreviewHtmlQuery query,
        User user)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var applicationId = new ApplicationId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            query.ApplicationReference,
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        _applicationRepo.Query().Returns(applications);

        _permissionCheckerService.HasPermission(
            ResourceType.Application,
            applicationId.Value.ToString(),
            AccessType.Read)
            .Returns(true);

        SetupTenantContext(null, null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Frontend base URL is not configured", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_ReturnFailure_When_InternalServiceAuthNotConfigured(
        GenerateApplicationPreviewHtmlQuery query,
        User user)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var applicationId = new ApplicationId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            query.ApplicationReference,
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        _applicationRepo.Query().Returns(applications);

        _permissionCheckerService.HasPermission(
            ResourceType.Application,
            applicationId.Value.ToString(),
            AccessType.Read)
            .Returns(true);

        // Setup tenant context without internal service auth configured
        SetupTenantContext("https://test.com", null, null, null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Internal service authentication is not configured", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_PassCorrectUrlToHtmlGeneratorService(
        GenerateApplicationPreviewHtmlQuery query,
        User user)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var applicationId = new ApplicationId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            query.ApplicationReference,
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        _applicationRepo.Query().Returns(applications);

        _permissionCheckerService.HasPermission(
            ResourceType.Application,
            applicationId.Value.ToString(),
            AccessType.Read)
            .Returns(true);

        var frontendBaseUrl = "https://frontend.test.com";
        SetupTenantContext(frontendBaseUrl, null, "test-service@test.com", "test-key");

        _htmlGeneratorService.GenerateStaticHtmlAsync(
            Arg.Any<string>(),
            Arg.Any<IDictionary<string, string>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns("<html></html>");

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        var expectedUrl = $"{frontendBaseUrl}/applications/{query.ApplicationReference}?preview=true";
        await _htmlGeneratorService.Received(1).GenerateStaticHtmlAsync(
            expectedUrl,
            Arg.Any<IDictionary<string, string>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_Should_PassCorrectAuthenticationHeaders(
        GenerateApplicationPreviewHtmlQuery query,
        User user)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var applicationId = new ApplicationId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            query.ApplicationReference,
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        _applicationRepo.Query().Returns(applications);

        _permissionCheckerService.HasPermission(
            ResourceType.Application,
            applicationId.Value.ToString(),
            AccessType.Read)
            .Returns(true);

        var expectedEmail = "test-service@test.com";
        var expectedApiKey = "test-api-key";
        SetupTenantContext("https://test.com", null, expectedEmail, expectedApiKey);

        _htmlGeneratorService.GenerateStaticHtmlAsync(
            Arg.Any<string>(),
            Arg.Any<IDictionary<string, string>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns("<html></html>");

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        await _htmlGeneratorService.Received(1).GenerateStaticHtmlAsync(
            Arg.Any<string>(),
            Arg.Is<IDictionary<string, string>>(headers =>
                headers["x-service-email"] == expectedEmail &&
                headers["x-service-api-key"] == expectedApiKey),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private void SetupTenantContext(
        string? frontendBaseUrl, 
        string? contentSelector, 
        string? serviceEmail = "test-service@test.com", 
        string? serviceApiKey = "test-api-key")
    {
        var configData = new Dictionary<string, string?>();
        
        if (frontendBaseUrl != null)
            configData["FrontendSettings:BaseUrl"] = frontendBaseUrl;
        if (contentSelector != null)
            configData["FrontendSettings:PreviewContentSelector"] = contentSelector;
        if (serviceEmail != null && serviceApiKey != null)
        {
            configData["InternalServiceAuth:Services:0:Email"] = serviceEmail;
            configData["InternalServiceAuth:Services:0:ApiKey"] = serviceApiKey;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var tenant = new TenantConfiguration(
            Guid.NewGuid(),
            "TestTenant",
            configuration,
            Array.Empty<string>());

        _tenantContextAccessor.CurrentTenant.Returns(tenant);
    }
}

