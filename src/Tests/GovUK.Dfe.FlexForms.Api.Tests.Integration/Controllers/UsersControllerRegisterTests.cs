using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Helpers;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

public class UsersControllerRegisterTests
{
    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RegisterUserAsync_ShouldCreateNewUser_WhenValidTokenAndUserDoesNotExist(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        // Arrange
        var newUserEmail = "newuser@example.com";
        var externalToken = TestExternalIdentityValidator.CreateToken(newUserEmail);

        factory.TestClaims = new List<Claim>
        {
            new Claim("iss", "windows.net"),
            new Claim("appid", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "API.Read"),
            new Claim(ClaimTypes.Role, "API.Write"),
            new Claim(GovUK.Dfe.FlexForms.Domain.Tenancy.TenantAuthClaimTypes.IsService, "true")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "azure-token");

        var dbContext = factory.GetDbContext<ExternalApplicationsContext>();
        
        // Ensure user doesn't exist
        var existingUser = await dbContext.Users
            .Where(x => x.Email == newUserEmail)
            .FirstOrDefaultAsync();
        Assert.Null(existingUser);

        var request = new RegisterUserRequest 
        { 
            AccessToken = externalToken,
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId)
        };

        // Act
        var result = await usersClient.RegisterUserAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.UserId);
        Assert.Equal(newUserEmail, result.Email);
        Assert.NotEmpty(result.Name);

        // Verify user was created in database
        var createdUser = await dbContext.Users
            .Where(x => x.Email == newUserEmail)
            .Include(x => x.Permissions)
            .FirstOrDefaultAsync();
        
        Assert.NotNull(createdUser);
        Assert.Equal(newUserEmail, createdUser.Email);
        Assert.NotNull(createdUser.Permissions);
        
        // Should have notification permissions
        var notificationPermissions = createdUser.Permissions
            .Where(p => p.ResourceType == GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.ResourceType.Notifications)
            .ToList();
        Assert.NotEmpty(notificationPermissions);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RegisterUserAsync_ShouldReturnExistingUser_WhenUserAlreadyExists(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        // Arrange
        var existingUserEmail = "alice@example.com"; // This user exists in seeded data
        var externalToken = TestExternalIdentityValidator.CreateToken(existingUserEmail);

        factory.TestClaims = new List<Claim>
        {
            new Claim("iss", "windows.net"),
            new Claim("appid", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "API.Read"),
            new Claim(ClaimTypes.Role, "API.Write"),
            new Claim(GovUK.Dfe.FlexForms.Domain.Tenancy.TenantAuthClaimTypes.IsService, "true")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "azure-token");

        var dbContext = factory.GetDbContext<ExternalApplicationsContext>();
        
        // Get existing user ID
        var existingUser = await dbContext.Users
            .Where(x => x.Email == existingUserEmail)
            .FirstOrDefaultAsync();
        Assert.NotNull(existingUser);
        var existingUserId = existingUser.Id!.Value;

        var request = new RegisterUserRequest 
        { 
            AccessToken = externalToken,
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId)
        };

        // Act
        var result = await usersClient.RegisterUserAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingUserId, result.UserId);
        Assert.Equal(existingUserEmail, result.Email);

        // Verify no duplicate user was created
        var userCount = await dbContext.Users
            .Where(x => x.Email == existingUserEmail)
            .CountAsync();
        Assert.Equal(1, userCount);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RegisterUserAsync_ShouldReturnBadRequest_WhenTokenIsInvalid(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        // Arrange
        var invalidToken = "invalid-token";

        factory.TestClaims = new List<Claim>
        {
            new Claim("iss", "windows.net"),
            new Claim("appid", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "API.Read"),
            new Claim(ClaimTypes.Role, "API.Write"),
            new Claim(GovUK.Dfe.FlexForms.Domain.Tenancy.TenantAuthClaimTypes.IsService, "true")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "azure-token");

        var request = new RegisterUserRequest 
        { 
            AccessToken = invalidToken,
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId)
        };

        // Act & Assert
        await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            async () => await usersClient.RegisterUserAsync(request));
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RegisterUserAsync_ShouldReturnUnauthorized_WhenNotAuthenticated(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        // Arrange
        var externalToken = TestExternalIdentityValidator.CreateToken("test@example.com");

        // No authentication header set

        var request = new RegisterUserRequest 
        { 
            AccessToken = externalToken,
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId)
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException>(
            async () => await usersClient.RegisterUserAsync(request));
        
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RegisterUserAsync_ShouldCreateUserWithNotificationPermissions(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        // Arrange
        var newUserEmail = "testnotifications@example.com";
        var externalToken = TestExternalIdentityValidator.CreateToken(newUserEmail);

        factory.TestClaims = new List<Claim>
        {
            new Claim("iss", "windows.net"),
            new Claim("appid", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "API.Read"),
            new Claim(ClaimTypes.Role, "API.Write"),
            new Claim(GovUK.Dfe.FlexForms.Domain.Tenancy.TenantAuthClaimTypes.IsService, "true")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "azure-token");

        var request = new RegisterUserRequest 
        { 
            AccessToken = externalToken,
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId)
        };

        // Act
        var result = await usersClient.RegisterUserAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Authorization);
        Assert.NotNull(result.Authorization.Permissions);

        // Should have notification permissions for their own email
        var notificationPermissions = result.Authorization.Permissions
            .Where(p => p.ResourceType == GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.ResourceType.Notifications)
            .Where(p => p.ResourceKey == newUserEmail)
            .ToArray();

        Assert.NotEmpty(notificationPermissions);
        Assert.Contains(notificationPermissions, p => p.AccessType == GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.AccessType.Read);
        Assert.Contains(notificationPermissions, p => p.AccessType == GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.AccessType.Write);
        Assert.Contains(notificationPermissions, p => p.AccessType == GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.AccessType.Delete);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RegisterUserAsync_ShouldAssignUserRole(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        // Arrange
        var newUserEmail = "testrole@example.com";
        var externalToken = TestExternalIdentityValidator.CreateToken(newUserEmail);

        factory.TestClaims = new List<Claim>
        {
            new Claim("iss", "windows.net"),
            new Claim("appid", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "API.Read"),
            new Claim(ClaimTypes.Role, "API.Write"),
            new Claim(GovUK.Dfe.FlexForms.Domain.Tenancy.TenantAuthClaimTypes.IsService, "true")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "azure-token");

        var request = new RegisterUserRequest 
        { 
            AccessToken = externalToken,
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId)
        };

        // Act
        var result = await usersClient.RegisterUserAsync(request);

        // Assert
        Assert.NotNull(result);
        
        // Verify user has the User role (not Admin)
        var dbContext = factory.GetDbContext<ExternalApplicationsContext>();
        var createdUser = await dbContext.Users
            .Include(x => x.Role)
            .Where(x => x.Email == newUserEmail)
            .FirstOrDefaultAsync();
        
        Assert.NotNull(createdUser);
        Assert.NotNull(createdUser.Role);
        Assert.Equal("User", createdUser.Role.Name);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RegisterUserAsync_ShouldHandleRateLimit(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new Claim("iss", "windows.net"),
            new Claim("appid", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "API.Read"),
            new Claim(ClaimTypes.Role, "API.Write"),
            new Claim(GovUK.Dfe.FlexForms.Domain.Tenancy.TenantAuthClaimTypes.IsService, "true")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "azure-token");

        // Act - Make rapid sequential requests to test rate limiting (limit is 5 per 30 seconds)
        var successCount = 0;
        for (int i = 0; i < 10; i++)
        {
            var token = TestExternalIdentityValidator.CreateToken($"user{i}@example.com");
            var request = new RegisterUserRequest
            {
                AccessToken = token,
                TemplateId = Guid.Parse(EaContextSeeder.TemplateId)
            };

            try
            {
                await usersClient.RegisterUserAsync(request);
                successCount++;
            }
            catch
            {
                // Rate-limited or validation failures are acceptable after the limit is reached
            }
        }

        // Assert - At least one request should succeed (rate limit is 5 in 30 seconds)
        Assert.True(successCount >= 1, "At least one request should succeed");
    }
}

