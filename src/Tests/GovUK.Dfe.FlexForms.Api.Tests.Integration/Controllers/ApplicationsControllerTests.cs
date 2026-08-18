using System.Collections.ObjectModel;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

public class ApplicationsControllerTests
{
    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateApplicationAsync_ShouldCreateApplication_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string initialResponseBody)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Template:{EaContextSeeder.TemplateId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new CreateApplicationRequest
        {
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId),
            InitialResponseBody = initialResponseBody
        };

        // Act
        var result = await applicationsClient.CreateApplicationAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("TRF-", result.ApplicationReference);
        Assert.NotNull(result.TemplateSchema);
        Assert.NotEqual(Guid.Empty, result.TemplateSchema.TemplateId);
        Assert.NotEqual(Guid.Empty, result.TemplateSchema.TemplateVersionId);
        Assert.NotEmpty(result.TemplateSchema.VersionNumber);
        Assert.NotEmpty(result.TemplateSchema.JsonSchema);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddApplicationResponseAsync_ShouldAddResponse_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string responseBody)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var encodedBody = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(responseBody));
        var request = new AddApplicationResponseRequest
        {
            ResponseBody = encodedBody
        };

        // Act
        var response = await applicationsClient.AddApplicationResponseAsync(new Guid(EaContextSeeder.ApplicationId), request);

        // Assert
        Assert.NotNull(response);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddApplicationResponseAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,

        HttpClient httpClient,
        string responseBody)
    {
        // Arrange
        var request = new AddApplicationResponseRequest
        {
            ResponseBody = responseBody
        };
        var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        // Act
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.AddApplicationResponseAsync(new Guid(EaContextSeeder.ApplicationId), request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddApplicationResponseAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,

        HttpClient httpClient,
        string responseBody)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
            // No permission claim
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new AddApplicationResponseRequest
        {
            ResponseBody = responseBody
        };
        // Act - Auth middleware returns 403 with ExceptionResponse JSON so client gets typed exception.
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.AddApplicationResponseAsync(new Guid(EaContextSeeder.ApplicationId), request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddApplicationResponseAsync_ShouldReturnBadRequest_WhenInvalidData(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,

        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new AddApplicationResponseRequest
        {
            ResponseBody = string.Empty
        };
        // Act
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.AddApplicationResponseAsync(new Guid(EaContextSeeder.ApplicationId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddApplicationResponseAsync_ShouldReturnBadRequest_WhenBodyIsNotBase64(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new AddApplicationResponseRequest
        {
            ResponseBody = "this is not base64"
        };
        
        // Act
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.AddApplicationResponseAsync(new Guid(EaContextSeeder.ApplicationId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateApplicationAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        CreateApplicationRequest request)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.CreateApplicationAsync(request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateApplicationAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        CreateApplicationRequest request)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.CreateApplicationAsync(request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateApplicationAsync_ShouldReturnBadRequest_WhenInvalidData(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Template:{EaContextSeeder.TemplateId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new CreateApplicationRequest
        {
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId),
            InitialResponseBody = string.Empty
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.CreateApplicationAsync(request));
        Assert.Equal(400, ex.StatusCode);
    }



    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetMyApplicationsAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.GetMyApplicationsAsync(includeSchema: null));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetMyApplicationsAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.GetMyApplicationsAsync(includeSchema: null));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetApplicationByReferenceAsync_ShouldReturnApplicationDetails_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await applicationsClient.GetApplicationByReferenceAsync(EaContextSeeder.ApplicationReference);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(EaContextSeeder.ApplicationReference, result.ApplicationReference);
        Assert.NotNull(result.TemplateSchema);
        Assert.NotEqual(Guid.Empty, result.TemplateSchema.TemplateId);
        Assert.NotEqual(Guid.Empty, result.TemplateSchema.TemplateVersionId);
        Assert.NotEmpty(result.TemplateSchema.VersionNumber);
        Assert.NotEmpty(result.TemplateSchema.JsonSchema);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetApplicationByReferenceAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Act
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.GetApplicationByReferenceAsync(EaContextSeeder.ApplicationReference));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetApplicationByReferenceAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
            // No permission claim
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.GetApplicationByReferenceAsync(EaContextSeeder.ApplicationReference));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetApplicationByReferenceAsync_ShouldReturnNotFound_WhenApplicationNotExists(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
                 var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
             () => applicationsClient.GetApplicationByReferenceAsync("InvalidAppRef"));
         Assert.Equal(404, ex.StatusCode);
     }

     [Theory]
     [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
     public async Task SubmitApplicationAsync_ShouldSubmitApplication_WhenValidRequest(
         CustomWebApplicationDbContextFactory<Program> factory,
         IApplicationsClient applicationsClient,
         HttpClient httpClient)
     {
         // Arrange
         factory.TestClaims = new List<Claim>
         {
             new(ClaimTypes.Email, EaContextSeeder.BobEmail),
             new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
         };

         httpClient.DefaultRequestHeaders.Authorization =
             new AuthenticationHeaderValue("Bearer", "user-token");

         var applicationId = Guid.Parse(EaContextSeeder.ApplicationId);

         // Act
         var result = await applicationsClient.SubmitApplicationAsync(applicationId);

         // Assert
         Assert.NotNull(result);
         Assert.Equal(applicationId, result.ApplicationId);
         Assert.Equal(ApplicationStatus.Submitted, result.Status);
         Assert.NotNull(result.DateSubmitted);
     }

     [Theory]
     [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
     public async Task SubmitApplicationAsync_ShouldReturnUnauthorized_WhenTokenMissing(
         CustomWebApplicationDbContextFactory<Program> factory,
         IApplicationsClient applicationsClient,
         HttpClient httpClient)
     {
         // Arrange
         var applicationId = Guid.Parse(EaContextSeeder.ApplicationId);

         // Act
         var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
             () => applicationsClient.SubmitApplicationAsync(applicationId));
         Assert.Equal(403, ex.StatusCode);
     }

     [Theory]
     [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
     public async Task SubmitApplicationAsync_ShouldReturnForbidden_WhenPermissionMissing(
         CustomWebApplicationDbContextFactory<Program> factory,
         IApplicationsClient applicationsClient,
         HttpClient httpClient)
     {
         // Arrange
         factory.TestClaims = new List<Claim>
         {
             new(ClaimTypes.Email, EaContextSeeder.BobEmail)
             // No Write permission for this application
         };

         httpClient.DefaultRequestHeaders.Authorization =
             new AuthenticationHeaderValue("Bearer", "user-token");

         var applicationId = Guid.Parse(EaContextSeeder.ApplicationId);

         // Act
         var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
             () => applicationsClient.SubmitApplicationAsync(applicationId));
         Assert.Equal(403, ex.StatusCode);
     }

     [Theory]
     [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
     public async Task SubmitApplicationAsync_ShouldReturnNotFound_WhenApplicationNotExists(
         CustomWebApplicationDbContextFactory<Program> factory,
         IApplicationsClient applicationsClient,
         HttpClient httpClient)
     {
         // Arrange
         var nonExistentApplicationId = Guid.NewGuid();
         
         factory.TestClaims = new List<Claim>
         {
             new(ClaimTypes.Email, EaContextSeeder.BobEmail),
             new("permission", $"Application:{nonExistentApplicationId}:Write") // Give permission for the specific non-existent app
         };

         httpClient.DefaultRequestHeaders.Authorization =
             new AuthenticationHeaderValue("Bearer", "user-token");

         // Act
         var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
             () => applicationsClient.SubmitApplicationAsync(nonExistentApplicationId));
         Assert.Equal(404, ex.StatusCode);
     }

     [Theory]
     [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
     public async Task SubmitApplicationAsync_ShouldReturnBadRequest_WhenApplicationAlreadySubmitted(
         CustomWebApplicationDbContextFactory<Program> factory,
         IApplicationsClient applicationsClient,
         HttpClient httpClient)
     {
         // Arrange
         factory.TestClaims = new List<Claim>
         {
             new(ClaimTypes.Email, EaContextSeeder.BobEmail),
             new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
         };

         httpClient.DefaultRequestHeaders.Authorization =
             new AuthenticationHeaderValue("Bearer", "user-token");

         var applicationId = Guid.Parse(EaContextSeeder.ApplicationId);

         // First submission
         await applicationsClient.SubmitApplicationAsync(applicationId);

         // Act - Try to submit again
         var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
             () => applicationsClient.SubmitApplicationAsync(applicationId));
         Assert.Equal(400, ex.StatusCode);
     }

     [Theory]
     [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
     public async Task SubmitApplicationAsync_ShouldReturnForbidden_WhenUserIsNotApplicationCreator(
         CustomWebApplicationDbContextFactory<Program> factory,
         IApplicationsClient applicationsClient,
         HttpClient httpClient)
     {
         // Arrange - Use Alice's email (Alice exists but didn't create the application - Bob did)
         factory.TestClaims = new List<Claim>
         {
             new(ClaimTypes.Email, "alice@example.com"), // Alice exists but didn't create the application
             new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
         };

         httpClient.DefaultRequestHeaders.Authorization =
             new AuthenticationHeaderValue("Bearer", "user-token");

         var applicationId = Guid.Parse(EaContextSeeder.ApplicationId);

         // Act
         var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
             () => applicationsClient.SubmitApplicationAsync(applicationId));
         Assert.Equal(403, ex.StatusCode);
     }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetMyApplicationsAsync_ShouldReturnApplicationsWithoutSchema_WhenIncludeSchemaIsDefault(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", "Application:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act - Testing default behavior (includeSchema defaults to false when null)
        var result = await applicationsClient.GetMyApplicationsAsync(includeSchema: null);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, app =>
        {
            Assert.Null(app.TemplateSchema);
        });
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetMyApplicationsAsync_ShouldReturnApplicationsWithSchema_WhenIncludeSchemaIsTrue(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", "Application:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var result = await applicationsClient.GetMyApplicationsAsync(includeSchema: true);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, app =>
        {
            Assert.NotNull(app.TemplateSchema);
            Assert.NotEqual(Guid.Empty, app.TemplateSchema.TemplateId);
            Assert.NotEqual(Guid.Empty, app.TemplateSchema.TemplateVersionId);
            Assert.NotEmpty(app.TemplateSchema.VersionNumber);
            Assert.NotEmpty(app.TemplateSchema.JsonSchema);
        });
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetMyApplicationsAsync_ShouldReturnApplicationsWithoutSchema_WhenIncludeSchemaIsFalse(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", "Application:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var result = await applicationsClient.GetMyApplicationsAsync(includeSchema: false);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, app =>
        {
            Assert.Null(app.TemplateSchema);
        });
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetApplicationsForUserAsync_ShouldReturnApplicationsWithSchema_WhenIncludeSchemaIsTrue(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", "Application:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var result = await applicationsClient.GetApplicationsForUserAsync(EaContextSeeder.BobEmail, includeSchema: true);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, app => 
        {
            Assert.NotNull(app.TemplateSchema);
            Assert.NotEqual(Guid.Empty, app.TemplateSchema.TemplateId);
            Assert.NotEqual(Guid.Empty, app.TemplateSchema.TemplateVersionId);
            Assert.NotEmpty(app.TemplateSchema.VersionNumber);
            Assert.NotEmpty(app.TemplateSchema.JsonSchema);
        });
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetApplicationsForUserAsync_ShouldReturnApplicationsWithoutSchema_WhenIncludeSchemaIsFalse(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", "Application:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var result = await applicationsClient.GetApplicationsForUserAsync(EaContextSeeder.BobEmail, includeSchema: false);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, app => 
        {
            Assert.Null(app.TemplateSchema);
        });
     }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetContributorsAsync_ShouldReturnContributors_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await applicationsClient.GetContributorsAsync(new Guid(EaContextSeeder.ApplicationId));

        // Assert
        Assert.NotNull(result);
        // Note: This will be empty initially since no contributors are seeded
        Assert.IsType<ObservableCollection<UserDto>>(result);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetContributorsAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.GetContributorsAsync(new Guid(EaContextSeeder.ApplicationId)));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetContributorsAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
            // No permission claim
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.GetContributorsAsync(new Guid(EaContextSeeder.ApplicationId)));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddContributorAsync_ShouldAddContributor_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Use unique email to avoid conflicts with previous test runs
        // Format: name.timestamp@example.com to ensure uniqueness
        var uniqueEmail = $"john.doe.{DateTime.UtcNow.Ticks}@example.com";
        var request = new AddContributorRequest
        {
            Name = "John Doe",
            Email = uniqueEmail
        };

        // Act & Assert
        // The handler should succeed whether the contributor is new or already exists
        // If they exist, it will grant permissions; if new, it will create them
        var result = await applicationsClient.AddContributorAsync(new Guid(EaContextSeeder.ApplicationId), request);

        Assert.NotNull(result);
        Assert.Equal("John Doe", result.Name);
        Assert.Equal(uniqueEmail, result.Email);
        Assert.NotNull(result.Authorization);
        Assert.NotNull(result.Authorization.Permissions);
        Assert.NotNull(result.Authorization.Roles);
        Assert.NotEmpty(result.Authorization.Permissions);
        Assert.NotEmpty(result.Authorization.Roles);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddContributorAsync_ShouldGrantContributorFullWriteAccessToApplication_WhenAddedByOwner(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        IUsersClient usersClient,
        HttpClient httpClient,
        string responseBody)
    {
        var applicationId = new Guid(EaContextSeeder.ApplicationId);
        var uniqueEmail = $"contributor-write-{Guid.NewGuid()}@example.com";

        factory.TestClaims =
        [
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
        ];
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var contributor = await applicationsClient.AddContributorAsync(
            applicationId,
            new AddContributorRequest
            {
                Name = "Contributor User",
                Email = uniqueEmail
            });

        Assert.NotNull(contributor.Authorization?.Permissions);
        Assert.Contains(
            contributor.Authorization.Permissions,
            p => p.ResourceType == ResourceType.Application
                 && p.ResourceKey == EaContextSeeder.ApplicationId
                 && p.AccessType == AccessType.Read);
        Assert.Contains(
            contributor.Authorization.Permissions,
            p => p.ResourceType == ResourceType.Application
                 && p.ResourceKey == EaContextSeeder.ApplicationId
                 && p.AccessType == AccessType.Write);
        Assert.Contains(
            contributor.Authorization.Permissions,
            p => p.ResourceType == ResourceType.ApplicationFiles
                 && p.ResourceKey == EaContextSeeder.ApplicationId
                 && p.AccessType == AccessType.Write);

        var dbContext = factory.GetDbContext<ExternalApplicationsContext>();
        factory.TestClaims = BuildPermissionClaimsForUser(dbContext, uniqueEmail);
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var permissions = await usersClient.GetMyPermissionsAsync();

        Assert.NotNull(permissions.Permissions);
        Assert.Contains(
            permissions.Permissions,
            p => p.ResourceType == ResourceType.Application
                 && p.ResourceKey == EaContextSeeder.ApplicationId
                 && p.AccessType == AccessType.Write);
        Assert.Contains(
            permissions.Permissions,
            p => p.ResourceType == ResourceType.ApplicationFiles
                 && p.ResourceKey == EaContextSeeder.ApplicationId
                 && p.AccessType == AccessType.Write);
        Assert.Contains(
            permissions.Permissions,
            p => p.ResourceType == ResourceType.Template
                 && p.ResourceKey == EaContextSeeder.TemplateId
                 && p.AccessType == AccessType.Read);
        Assert.DoesNotContain(
            permissions.Permissions,
            p => p.ResourceType == ResourceType.Template
                 && p.ResourceKey == EaContextSeeder.TemplateId
                 && p.AccessType == AccessType.Write);

        var encodedBody = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(responseBody));
        var response = await applicationsClient.AddApplicationResponseAsync(
            applicationId,
            new AddApplicationResponseRequest { ResponseBody = encodedBody });

        Assert.NotNull(response);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddContributorAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient)
    {
        // Arrange
        var request = new AddContributorRequest
        {
            Name = "John Doe",
            Email = "john.doe@example.com"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.AddContributorAsync(new Guid(EaContextSeeder.ApplicationId), request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddContributorAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
            // No permission claim
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new AddContributorRequest
        {
            Name = "John Doe",
            Email = "john.doe@example.com"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.AddContributorAsync(new Guid(EaContextSeeder.ApplicationId), request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AddContributorAsync_ShouldReturnBadRequest_WhenInvalidData(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new AddContributorRequest
        {
            Name = "",
            Email = "invalid-email"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.AddContributorAsync(new Guid(EaContextSeeder.ApplicationId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RemoveContributorAsync_ShouldRemoveContributor_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write"),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // First, add a contributor
        var addRequest = new AddContributorRequest
        {
            Name = "John Doe",
            Email = "john.doe@example.com"
        };

        var addedContributor = await applicationsClient.AddContributorAsync(new Guid(EaContextSeeder.ApplicationId), addRequest);
        Assert.NotNull(addedContributor);

        // Act - Remove the contributor we just added
        await applicationsClient.RemoveContributorAsync(new Guid(EaContextSeeder.ApplicationId), addedContributor.UserId);

        // Assert
        // The endpoint should return 200 OK if successful
        // We can also verify the contributor was removed by trying to get contributors
        var contributors = await applicationsClient.GetContributorsAsync(new Guid(EaContextSeeder.ApplicationId));
        
        // Check if our specific contributor was removed
        var removedContributor = contributors.FirstOrDefault(c => c.UserId == addedContributor.UserId);
        Assert.Null(removedContributor);
        
        // Also verify that other contributors (if any) are still there
        var otherContributors = contributors.Where(c => c.UserId != addedContributor.UserId).ToList();
        Assert.Contains(otherContributors, c => c.Email == "alice@example.com");
        Assert.DoesNotContain(otherContributors, c => c.Email == "bob@example.com");
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RemoveContributorAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient)
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.RemoveContributorAsync(new Guid(EaContextSeeder.ApplicationId), userId));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task RemoveContributorAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
            // No permission claim
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var userId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.RemoveContributorAsync(new Guid(EaContextSeeder.ApplicationId), userId));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldUploadFile_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string description)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };

        var fileName = "test-file.jpg";

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Create a test file
        var fileContent = "Test file content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var result = await applicationsClient.UploadFileAsync(
            new Guid(EaContextSeeder.ApplicationId),
            fileName,
            description,
            fileParameter);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(new Guid(EaContextSeeder.ApplicationId), result.ApplicationId);
        Assert.Equal(fileName, result.Name);
        Assert.Equal(description, result.Description);
        Assert.Equal(fileName, result.OriginalFileName);
        Assert.NotNull(result.FileName);
        Assert.NotEmpty(result.FileName);
        Assert.NotEqual(default(DateTime), result.UploadedOn);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string fileName,
        string description)
    {
        // Arrange
        var fileContent = "Test file content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                fileName,
                description,
                fileParameter));

        // Assert
        Assert.Equal(403, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string fileName,
        string description)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
            // No permission claim
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var fileContent = "Test file content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                fileName,
                description,
                fileParameter));

        // Assert
        Assert.Equal(403, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldReturnBadRequest_WhenNoFileProvided(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string fileName,
        string description)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act - No file parameter
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                fileName,
                description,
                null));

        // Assert
        Assert.Equal(400, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldReturnBadRequest_WhenEmptyFile(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string fileName,
        string description)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Create an empty file
        var stream = new MemoryStream();
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                fileName,
                description,
                fileParameter));

        // Assert
        Assert.Equal(400, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldReturnNotFound_WhenApplicationNotExists(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string fileName,
        string description)
    {
        // Arrange
        var nonExistentApplicationId = Guid.NewGuid();
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{nonExistentApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var fileContent = "Test file content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                nonExistentApplicationId, // Use the same ID as in the permission claim
                fileName,
                description,
                fileParameter));

        // Assert
        Assert.Equal(404, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryWithExistingFileCustomization))]
    public async Task UploadFileAsync_ShouldReturnBadRequest_WhenFileAlreadyExists(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string description)
    {
        // Arrange - customization seeds an existing file "large-file.jpg" and a latest response body referencing it
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };
        var fileName = "large-file.jpg";

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var fileContent = "Test file content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act - upload same name as seeded file that is already referenced in the latest response → 409 Conflict
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                fileName,
                description,
                fileParameter));

        // Assert
        Assert.Equal(409, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldUploadFileWithNullDescription_WhenDescriptionIsNull(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };
        var fileName = "large-file.jpg";

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var fileContent = "Test file content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var result = await applicationsClient.UploadFileAsync(
            new Guid(EaContextSeeder.ApplicationId),
            fileName,
            null, // null description
            fileParameter);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.Description);
        Assert.Equal(fileName, result.Name);
        Assert.Equal(fileName, result.OriginalFileName);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldReturnBadRequest_WhenNameIsNull(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string description)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var fileName = "test.txt";
        var fileContent = "Test file content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                null, // null name - should be rejected
                description,
                fileParameter));

        // Assert
        Assert.Equal(400, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldHandleLargeFile_WhenFileIsLarge(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string description)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };

        var fileName = "large-file.jpg";
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Create a large file (1MB)
        var largeContent = new string('A', 1024 * 1024);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(largeContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var result = await applicationsClient.UploadFileAsync(
            new Guid(EaContextSeeder.ApplicationId),
            fileName,
            description,
            fileParameter);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(fileName, result.Name);
        Assert.Equal(description, result.Description);
        Assert.Equal(fileName, result.OriginalFileName);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldHandleDifferentFileTypes_WhenFileTypeIsValid(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string description)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Use unique filenames to avoid conflicts if files already exist from previous test runs
        // Use a shorter unique identifier to ensure filename stays under 255 character limit
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var testCases = new[]
        {
            new { FileName = $"document-{uniqueId}.pdf", ContentType = "application/pdf", Content = "%PDF-1.4\nTest PDF content" },
            new { FileName = $"image-{uniqueId}.jpg", ContentType = "image/jpeg", Content = "JPEG image content" },
            new { FileName = $"spreadsheet-{uniqueId}.xlsx", ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Content = "Excel content" },
            new { FileName = $"document-{uniqueId}.docx", ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Content = "Word document content" }
        };

        foreach (var testCase in testCases)
        {
            // Act - Create a new stream for each test case to ensure it's readable
            // Create a copy of the content bytes to ensure the stream can be read multiple times
            var contentBytes = System.Text.Encoding.UTF8.GetBytes(testCase.Content);
            
            // Ensure we have a valid filename - use the test case filename as both name and file parameter filename
            var fileName = testCase.FileName ?? "test-file.pdf";
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "test-file.pdf";
            
            // Create the stream fresh for each iteration with position at start
            var stream = new MemoryStream(contentBytes);
            var fileParameter = new FileParameter(stream, fileName, testCase.ContentType);

            // Name is required and must be non-empty for validation to pass
            // Pass empty string instead of null for description to ensure it's sent
            var result = await applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                fileName, // Use the same filename for name
                description ?? string.Empty, // Ensure description is not null
                fileParameter);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(fileName, result.Name);
            Assert.Equal(fileName, result.OriginalFileName);
            Assert.Equal(description ?? string.Empty, result.Description);
        }
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldReturnBadRequest_WhenFileSizeExceedsLimit(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string description)
    {
        // Arrange
        factory.TestClaims =
        [
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        ];
        var fileName = "large-file.jpg";

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Create a file that exceeds the 25MB limit (26MB)
        var largeContent = new string('A', 26 * 1024 * 1024);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(largeContent));
        var fileParameter = new FileParameter(stream, fileName, "text/plain");

        // Act
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                fileName,
                description,
                fileParameter));

        // Assert
        Assert.Equal(400, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task UploadFileAsync_ShouldReturnBadRequest_WhenFileExtensionNotAllowed(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient,
        string description)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var fileName = "script.exe"; // Not allowed extension
        var fileContent = "Executable content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        var fileParameter = new FileParameter(stream, fileName, "application/x-msdownload");

        // Act
        var exception = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            applicationsClient.UploadFileAsync(
                new Guid(EaContextSeeder.ApplicationId),
                fileName,
                description,
                fileParameter));

        // Assert
        Assert.Equal(400, exception.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetFilesForApplicationAsync_ShouldReturnFiles_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"ApplicationFiles:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await applicationsClient.GetFilesForApplicationAsync(new Guid(EaContextSeeder.ApplicationId));

        // Assert
        Assert.NotNull(result);
        // Should return empty list since no files exist yet
        Assert.Empty(result);
    }

    #region Rate Limiting Tests

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryWithRateLimitingCustomization))]
    public async Task SubmitApplicationAsync_ShouldReturn429_WhenRateLimitExceeded(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var applicationId = Guid.Parse(EaContextSeeder.ApplicationId);

        // First submission should work
        await applicationsClient.SubmitApplicationAsync(applicationId);

        // Act - Try to submit again immediately (should hit rate limit)
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.SubmitApplicationAsync(applicationId));

        // Assert
        Assert.Equal(429, ex.StatusCode);
        Assert.Contains("Too many requests", ex.Result?.Message ?? "");
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryWithRateLimitingCustomization))]
    public async Task CreateApplicationAsync_ShouldReturn429_WhenRateLimitExceeded(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Template:{EaContextSeeder.TemplateId}:Write")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request1 = new CreateApplicationRequest
        {
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId),
            InitialResponseBody = "First application"
        };

        var request2 = new CreateApplicationRequest
        {
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId),
            InitialResponseBody = "Second application"
        };

        // First three creations should work (rate limit is 3 per 30 seconds)
        await applicationsClient.CreateApplicationAsync(request1);
        await applicationsClient.CreateApplicationAsync(new CreateApplicationRequest
        {
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId),
            InitialResponseBody = "Second application"
        });
        await applicationsClient.CreateApplicationAsync(new CreateApplicationRequest
        {
            TemplateId = Guid.Parse(EaContextSeeder.TemplateId),
            InitialResponseBody = "Third application"
        });

        // Act - Fourth creation should hit the rate limit
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => applicationsClient.CreateApplicationAsync(request2));

        // Assert
        Assert.Equal(429, ex.StatusCode);
        Assert.Contains("Too many requests", ex.Result?.Message ?? "");
    }

    #endregion

    #region DownloadApplicationPreviewHtml

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task DownloadApplicationPreviewHtmlAsync_ShouldReturnHtmlFile_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act - Call the endpoint directly using HttpClient since this returns a file
        var response = await httpClient.GetAsync($"/v1/applications/reference/{EaContextSeeder.ApplicationReference}/preview/download");

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        
        // Verify content type
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        
        // Verify content disposition header
        var contentDisposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(contentDisposition);
        Assert.Equal("attachment", contentDisposition.DispositionType);
        Assert.Contains(EaContextSeeder.ApplicationReference, contentDisposition.FileName);
        Assert.EndsWith(".html", contentDisposition.FileName);
        
        // Verify content
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
        Assert.Contains("<!DOCTYPE html>", content);
        Assert.Contains("Mock Application Preview", content);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task DownloadApplicationPreviewHtmlAsync_ShouldReturnUnauthorized_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient httpClient)
    {
        // Arrange - No authorization header set

        // Act
        var response = await httpClient.GetAsync($"/v1/applications/reference/{EaContextSeeder.ApplicationReference}/preview/download");

        // Assert
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task DownloadApplicationPreviewHtmlAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
            // No permission claim
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var response = await httpClient.GetAsync($"/v1/applications/reference/{EaContextSeeder.ApplicationReference}/preview/download");

        // Assert
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task DownloadApplicationPreviewHtmlAsync_ShouldReturnNotFound_WhenApplicationDoesNotExist(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var nonExistentReference = "TRF-99999999-999";

        // Act
        var response = await httpClient.GetAsync($"/v1/applications/reference/{nonExistentReference}/preview/download");

        // Assert
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task DownloadApplicationPreviewHtmlAsync_ShouldIncludeApplicationReference_InFileName(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var response = await httpClient.GetAsync($"/v1/applications/reference/{EaContextSeeder.ApplicationReference}/preview/download");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        
        var contentDisposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(contentDisposition);
        
        // Verify filename format: application-{reference}-preview.html
        var expectedFileName = $"application-{EaContextSeeder.ApplicationReference}-preview.html";
        Assert.Contains(expectedFileName, contentDisposition.FileName);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task DownloadApplicationPreviewHtmlAsync_ShouldReturnValidHtmlContent(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var response = await httpClient.GetAsync($"/v1/applications/reference/{EaContextSeeder.ApplicationReference}/preview/download");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        
        var htmlContent = await response.Content.ReadAsStringAsync();
        
        // Verify it's valid HTML
        Assert.Contains("<!DOCTYPE html>", htmlContent);
        Assert.Contains("<html", htmlContent);
        Assert.Contains("</html>", htmlContent);
        Assert.Contains("<head>", htmlContent);
        Assert.Contains("<body", htmlContent);
        
        // Verify mock service content
        Assert.Contains("Mock Application Preview", htmlContent);
        Assert.Contains("govuk-template", htmlContent);
    }

    #endregion

    #region GetMyApplications

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetMyApplicationsAsync_ShouldReturnPagedApplications_WhenUserHasPermissions(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", "Application:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await applicationsClient.GetMyApplicationsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.TotalPages);
        Assert.Single(result.Items);
        Assert.Equal(EaContextSeeder.ApplicationReference, result.Items.First().ApplicationReference);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetMyApplicationsAsync_ShouldReturnPagedResults_WhenPaginationParamsProvided(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", "Application:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await applicationsClient.GetMyApplicationsAsync(pageNumber: 1, pageSize: 1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.Single(result.Items);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetMyApplicationsAsync_ShouldReturnEmptyPage_WhenPageNumberExceedsTotal(
        CustomWebApplicationDbContextFactory<Program> factory,
        IApplicationsClient applicationsClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", "Application:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        // Act
        var result = await applicationsClient.GetMyApplicationsAsync(pageNumber: 99, pageSize: 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(99, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Empty(result.Items);
    }

    #endregion

    private static List<Claim> BuildPermissionClaimsForUser(ExternalApplicationsContext dbContext, string email)
    {
        var user = dbContext.Users
            .Include(u => u.Permissions)
            .Single(u => u.Email == email);

        var claims = new List<Claim> { new(ClaimTypes.Email, email) };

        foreach (var permission in user.Permissions)
        {
            claims.Add(new Claim(
                "permission",
                $"{permission.ResourceType}:{permission.ResourceKey}:{permission.AccessType}"));
        }

        return claims;
    }
}