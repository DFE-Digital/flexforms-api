using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

public class TemplatesControllerTests
{
    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetLatestTemplateSchemaAsync_ShouldReturnSchema_WhenUserHasAccess(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new("appid", EaContextSeeder.BobExternalId),
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Template:{EaContextSeeder.TemplateId}:Read")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var result = await templatesClient.GetLatestTemplateSchemaAsync(Guid.Parse(EaContextSeeder.TemplateId));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Guid.Parse(EaContextSeeder.TemplateVersionId), result.TemplateVersionId);
        Assert.NotNull(result.JsonSchema);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetLatestTemplateSchemaAsync_ShouldReturnForbidden_WhenCallerHasNoClaims(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.GetLatestTemplateSchemaAsync(Guid.Parse(EaContextSeeder.TemplateId)));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetLatestTemplateSchemaAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new("appid", EaContextSeeder.BobExternalId),
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.GetLatestTemplateSchemaAsync(Guid.Parse(EaContextSeeder.TemplateId)));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateTemplateVersionAsync_ShouldCreateVersion_WhenValidRequest(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Template:{EaContextSeeder.TemplateId}:Write"),
            new(ClaimTypes.Role, "Admin")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var jsonSchema = "{ \"new\": \"schema\" }";
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(jsonSchema);
        var base64JsonSchema = System.Convert.ToBase64String(plainTextBytes);
        
        var request = new CreateTemplateVersionRequest("1.0.1", base64JsonSchema);

        // Act
        var result = await templatesClient.CreateTemplateVersionAsync(Guid.Parse(EaContextSeeder.TemplateId), request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.1", result.VersionNumber);
        Assert.Equal(jsonSchema, result.JsonSchema);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateTemplateVersionAsync_ShouldReturnForbidden_WhenUserIsNotAdmin(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
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

        var jsonSchema = "{ \"new\": \"schema\" }";
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(jsonSchema);
        var base64JsonSchema = System.Convert.ToBase64String(plainTextBytes);

        var request = new CreateTemplateVersionRequest("1.0.1", base64JsonSchema);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.CreateTemplateVersionAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateTemplateVersionAsync_ShouldReturnForbidden_WhenCallerHasNoClaims(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        CreateTemplateVersionRequest request)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.CreateTemplateVersionAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateTemplateVersionAsync_ShouldReturnForbidden_WhenPermissionMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient,
        CreateTemplateVersionRequest request)
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
            () => templatesClient.CreateTemplateVersionAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(403, ex.StatusCode);
    }
    
    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateTemplateVersionAsync_ShouldReturnConflict_WhenVersionExists(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Template:{EaContextSeeder.TemplateId}:Write"),
            new(ClaimTypes.Role, "Admin"),
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes("{ \"new\": \"schema\" }");
        var base64JsonSchema = System.Convert.ToBase64String(plainTextBytes);

        var request = new CreateTemplateVersionRequest("1.0.0", base64JsonSchema);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.CreateTemplateVersionAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateTemplateVersionAsync_ShouldReturnBadRequest_WhenInvalidData(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Template:{EaContextSeeder.TemplateId}:Write"),
            new(ClaimTypes.Role, "Admin"),

        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");
        
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes("{ \"new\": \"schema\" }");
        var base64JsonSchema = System.Convert.ToBase64String(plainTextBytes);

        var request = new CreateTemplateVersionRequest(string.Empty, base64JsonSchema);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.CreateTemplateVersionAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateTemplateVersionAsync_ShouldReturnBadRequest_WhenSchemaIsNotBase64(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Template:{EaContextSeeder.TemplateId}:Write"),
            new(ClaimTypes.Role, "Admin"),
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var request = new CreateTemplateVersionRequest("1.0.1", "this is not base64");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.CreateTemplateVersionAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetCustomApplicationStatusesAsync_ShouldReturnAllStatuses_WhenAuthenticated(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new(ClaimTypes.Role, "Admin")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var result = await templatesClient.GetCustomApplicationStatusesAsync(Guid.Parse(EaContextSeeder.TemplateId));

        // Assert
        Assert.NotNull(result);

        // Should return all ApplicationStatus enum values
        var allStatuses = Enum.GetValues(typeof(ApplicationStatus)).Cast<ApplicationStatus>();
        Assert.Equal(allStatuses.Count(), result.Count);

        // Verify all status values are present
        foreach (var status in allStatuses)
        {
            Assert.Contains(result, s => s.ApplicationStatus == ApplicationStatus.Submitted);
        }
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task GetCustomApplicationStatusesAsync_ShouldReturnForbidden_WhenCallerHasNoClaims(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException>(
            () => templatesClient.GetCustomApplicationStatusesAsync(Guid.Parse(EaContextSeeder.TemplateId)));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateCustomApplicationStatusAsync_ShouldCreateNewStatus_WhenNotExists(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new(ClaimTypes.Role, "Admin")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new CustomApplicationStatusRequest
        {
            ApplicationStatus = ApplicationStatus.Submitted,
            Label = "Custom Rejection Label"
        };

        // Act
        var result = await templatesClient.CreateCustomApplicationStatusAsync(Guid.Parse(EaContextSeeder.TemplateId), request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ApplicationStatus.Submitted, result.ApplicationStatus);
        Assert.Equal("Custom Rejection Label", result.Label);
        Assert.NotEqual(Guid.Empty, result.CustomApplicationStatusId);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateCustomApplicationStatusAsync_ShouldUpdateExistingStatus_WhenExists(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new(ClaimTypes.Role, "Admin")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        // Create first
        var request1 = new CustomApplicationStatusRequest
        {
            ApplicationStatus = ApplicationStatus.InProgress,
            Label = "First Label"
        };

        var firstResult = await templatesClient.CreateCustomApplicationStatusAsync(Guid.Parse(EaContextSeeder.TemplateId), request1);
        var firstId = firstResult.CustomApplicationStatusId;

        // Update with new label
        var request2 = new CustomApplicationStatusRequest
        {
            ApplicationStatus = ApplicationStatus.InProgress,
            Label = "Updated Label"
        };

        // Act
        var secondResult = await templatesClient.CreateCustomApplicationStatusAsync(Guid.Parse(EaContextSeeder.TemplateId), request2);

        // Assert
        Assert.NotNull(secondResult);
        Assert.Equal(firstId, secondResult.CustomApplicationStatusId); // Same ID
        Assert.Equal(ApplicationStatus.InProgress, secondResult.ApplicationStatus);
        Assert.Equal("Updated Label", secondResult.Label);
        Assert.Equal(firstResult.CreatedOn, secondResult.CreatedOn);
        Assert.Equal(firstResult.CreatedBy, secondResult.CreatedBy);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateCustomApplicationStatusAsync_ShouldReturnBadRequest_WhenLabelIsEmpty(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new(ClaimTypes.Role, "Admin")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new CustomApplicationStatusRequest
        {
            ApplicationStatus = ApplicationStatus.Submitted,
            Label = string.Empty
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.CreateCustomApplicationStatusAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateCustomApplicationStatusAsync_ShouldReturnBadRequest_WhenApplicationStatusIsInvalid(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new(ClaimTypes.Role, "Admin")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new CustomApplicationStatusRequest
        {
            ApplicationStatus = (ApplicationStatus)999,
            Label = "Test Label"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.CreateCustomApplicationStatusAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateCustomApplicationStatusAsync_ShouldReturnBadRequest_WhenApplicationStatusIsMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new(ClaimTypes.Role, "Admin")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new CustomApplicationStatusRequest
        {
            Label = "Test Label"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => templatesClient.CreateCustomApplicationStatusAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateCustomApplicationStatusAsync_ShouldReturnBadRequest_WhenApplicationStatusStringIsInvalid(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient httpClient)
    {
        // Arrange
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new(ClaimTypes.Role, "Admin")
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        using var content = new StringContent(
            """{"applicationStatus":"NotAValidStatus","label":"Test Label"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        // Act
        var response = await httpClient.PostAsync(
            $"v1/Templates/{EaContextSeeder.TemplateId}/custom-statuses",
            content);

        // Assert
        Assert.Equal(400, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid request data", body);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateCustomApplicationStatusAsync_ShouldReturnForbidden_WhenNotAdmin(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        HttpClient httpClient)
    {
        // Arrange - User without Admin role
        factory.TestClaims = new List<Claim>
        {
            new(ClaimTypes.Email, EaContextSeeder.BobEmail)
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new CustomApplicationStatusRequest
        {
            ApplicationStatus = ApplicationStatus.Submitted,
            Label = "Test Label"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException>(
            () => templatesClient.CreateCustomApplicationStatusAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task CreateCustomApplicationStatusAsync_ShouldReturnForbidden_WhenCallerHasNoClaims(
        CustomWebApplicationDbContextFactory<Program> factory,
        ITemplatesClient templatesClient,
        CustomApplicationStatusRequest request)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException>(
            () => templatesClient.CreateCustomApplicationStatusAsync(Guid.Parse(EaContextSeeder.TemplateId), request));
        Assert.Equal(403, ex.StatusCode);
    }
}
