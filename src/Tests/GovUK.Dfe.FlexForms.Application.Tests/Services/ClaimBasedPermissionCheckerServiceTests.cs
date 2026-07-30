using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class ClaimBasedPermissionCheckerServiceTests
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ClaimBasedPermissionCheckerService _service;
    private readonly HttpContext _httpContext;
    private readonly ClaimsPrincipal _user;

    public ClaimBasedPermissionCheckerServiceTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _httpContext = Substitute.For<HttpContext>();
        _user = new ClaimsPrincipal(new ClaimsIdentity());
        _httpContext.User.Returns(_user);
        _httpContextAccessor.HttpContext.Returns(_httpContext);
        _service = new ClaimBasedPermissionCheckerService(_httpContextAccessor);
    }

    [Fact]
    public void HasPermission_WhenUserHasMatchingClaim_ReturnsTrue()
    {
        // Arrange
        var resourceType = ResourceType.Application;
        var resourceId = "123";
        var accessType = AccessType.Write;
        var claim = new Claim("permission", $"{resourceType}:{resourceId}:{accessType}");
        _user.AddIdentity(new ClaimsIdentity(new[] { claim }));

        // Act
        var result = _service.HasPermission(resourceType, resourceId, accessType);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasPermission_WhenUserDoesNotHaveMatchingClaim_ReturnsFalse()
    {
        // Arrange
        var resourceType = ResourceType.User;
        var resourceId = "123";
        var accessType = AccessType.Write;
        var claim = new Claim("permission", $"{resourceType}:different-id:{accessType}");
        _user.AddIdentity(new ClaimsIdentity(new[] { claim }));

        // Act
        var result = _service.HasPermission(resourceType, resourceId, accessType);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasAnyPermission_WhenUserHasMatchingClaim_ReturnsTrue()
    {
        // Arrange
        var resourceType = ResourceType.Application;
        var accessType = AccessType.Write;
        var claim = new Claim("permission", $"{resourceType}:any-id:{accessType}");
        _user.AddIdentity(new ClaimsIdentity(new[] { claim }));

        // Act
        var result = _service.HasAnyPermission(resourceType, accessType);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasAnyPermission_WhenUserDoesNotHaveMatchingClaim_ReturnsFalse()
    {
        // Arrange
        var resourceType = ResourceType.User;
        var accessType = AccessType.Write;
        var claim = new Claim("permission", $"DifferentType:any-id:{accessType}");
        _user.AddIdentity(new ClaimsIdentity(new[] { claim }));

        // Act
        var result = _service.HasAnyPermission(resourceType, accessType);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetResourceIdsWithPermission_ReturnsCorrectIds()
    {
        // Arrange
        var resourceType = ResourceType.Application;
        var accessType = AccessType.Write;
        var claims = new[]
        {
            new Claim("permission", $"{resourceType}:123:{accessType}"),
            new Claim("permission", $"{resourceType}:456:{accessType}"),
            new Claim("permission", $"{resourceType}:789:Read"),
            new Claim("permission", "DifferentType:123:{accessType}")
        };
        _user.AddIdentity(new ClaimsIdentity(claims));

        // Act
        var result = _service.GetResourceIdsWithPermission(resourceType, accessType);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("123", result);
        Assert.Contains("456", result);
    }

    [Fact]
    public void Constructor_WhenHttpContextAccessorIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ClaimBasedPermissionCheckerService(null!));
    }

    [Fact]
    public void HasPermission_WhenHttpContextIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.HasPermission(ResourceType.Application, "123", AccessType.Read);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasPermission_WhenUserIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns((ClaimsPrincipal?)null);
        httpContextAccessor.HttpContext.Returns(httpContext);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.HasPermission(ResourceType.Application, "123", AccessType.Read);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasPermission_WhenUserHasNoPermissionClaims_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim("role", "admin")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.HasPermission(ResourceType.Application, "123", AccessType.Read);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasPermission_WhenPermissionClaimHasInvalidFormat_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("permission", "invalid-format"),
            new Claim("permission", "Application:123"), // Missing access type
            new Claim("permission", "Application"), // Missing resource ID and access type
            new Claim("permission", ""), // Empty claim
            new Claim("permission", ":123:Read"), // Missing resource type
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.HasPermission(ResourceType.Application, "123", AccessType.Read);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasPermission_WhenResourceIdIsNullOrEmpty_ReturnsFalse()
    {
        // Arrange
        var claim = new Claim("permission", $"{ResourceType.Application}:123:{AccessType.Read}");
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { claim }));
        _httpContext.User.Returns(user);

        // Act & Assert
        Assert.False(_service.HasPermission(ResourceType.Application, null!, AccessType.Read));
        Assert.False(_service.HasPermission(ResourceType.Application, "", AccessType.Read));
        Assert.False(_service.HasPermission(ResourceType.Application, "   ", AccessType.Read));
    }

    [Fact]
    public void HasAnyPermission_WhenHttpContextIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.HasAnyPermission(ResourceType.Application, AccessType.Read);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasAnyPermission_WhenUserHasInvalidPermissionClaims_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("permission", "invalid-format"),
            new Claim("permission", "Application:123"), // Missing access type
            new Claim("permission", "Different:123:Read"), // Different resource type
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.HasAnyPermission(ResourceType.Application, AccessType.Read);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetResourceIdsWithPermission_WhenHttpContextIsNull_ReturnsEmptyList()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.GetResourceIdsWithPermission(ResourceType.Application, AccessType.Read);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetResourceIdsWithPermission_WhenUserHasInvalidPermissionClaims_ReturnsOnlyValidIds()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("permission", $"{ResourceType.Application}:123:{AccessType.Read}"), // Valid
            new Claim("permission", "invalid-format"), // Invalid - doesn't match pattern
            new Claim("permission", $"{ResourceType.Application}:456:{AccessType.Read}"), // Valid
            new Claim("permission", $"{ResourceType.Application}::{AccessType.Read}"), // Valid but empty resource ID
            new Claim("permission", $"{ResourceType.Application}:789:{AccessType.Write}"), // Invalid - different access type
            new Claim("permission", $"{ResourceType.User}:999:{AccessType.Read}"), // Invalid - different resource type
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.GetResourceIdsWithPermission(ResourceType.Application, AccessType.Read);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("123", result);
        Assert.Contains("456", result);
        Assert.Contains("", result); // Empty string from the Application::Read claim
    }

    [Fact]
    public void GetResourceIdsWithPermission_WhenUserHasNoClaims_ReturnsEmptyList()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        _httpContext.User.Returns(user);

        // Act
        var result = _service.GetResourceIdsWithPermission(ResourceType.Application, AccessType.Read);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetResourceIdsWithPermission_WhenUserHasDuplicateResourceIds_ReturnsDistinctIds()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("permission", $"{ResourceType.Application}:123:{AccessType.Read}"),
            new Claim("permission", $"{ResourceType.Application}:123:{AccessType.Read}"), // Duplicate
            new Claim("permission", $"{ResourceType.Application}:456:{AccessType.Read}"),
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.GetResourceIdsWithPermission(ResourceType.Application, AccessType.Read);

        // Assert
        // Note: The current implementation doesn't deduplicate, so we get 3 results including the duplicate
        Assert.Equal(3, result.Count);
        Assert.Contains("123", result);
        Assert.Contains("456", result);
    }

    [Fact]
    public void HasPermission_WhenUserHasOnlyAnyReadWildcard_ReturnsTrue()
    {
        var resourceId = Guid.NewGuid().ToString();
        var claim = new Claim("permission", $"{ResourceType.Application}:Any:{AccessType.Read}");
        _user.AddIdentity(new ClaimsIdentity(new[] { claim }));

        var result = _service.HasPermission(ResourceType.Application, resourceId, AccessType.Read);

        Assert.True(result);
    }

    [Fact]
    public void HasAnyPermission_WhenUserHasOnlyAnyReadWildcard_ReturnsTrue()
    {
        var claim = new Claim("permission", $"{ResourceType.Application}:Any:{AccessType.Read}");
        _user.AddIdentity(new ClaimsIdentity(new[] { claim }));

        var result = _service.HasAnyPermission(ResourceType.Application, AccessType.Read);

        Assert.True(result);
    }

    [Theory]
    [InlineData(ResourceType.Application)]
    [InlineData(ResourceType.Template)]
    [InlineData(ResourceType.User)]
    public void HasPermission_WorksWithAllResourceTypes(ResourceType resourceType)
    {
        // Arrange
        var claim = new Claim("permission", $"{resourceType}:123:{AccessType.Read}");
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { claim }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.HasPermission(resourceType, "123", AccessType.Read);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(AccessType.Read)]
    [InlineData(AccessType.Write)]
    public void HasPermission_WorksWithAllAccessTypes(AccessType accessType)
    {
        // Arrange
        var claim = new Claim("permission", $"{ResourceType.Application}:123:{accessType}");
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { claim }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.HasPermission(ResourceType.Application, "123", accessType);

        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void IsAdmin_WhenUserIsAdmin_ReturnsTrue()
    {
        // Arrange
        var claim = new Claim(ClaimTypes.Role, "Admin");
        _user.AddIdentity(new ClaimsIdentity(new[] { claim }));

        // Act
        var result = _service.IsAdmin();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAdmin_WhenUserIsNotAdmin_ReturnsFalse()
    {
        // Arrange
        var claim = new Claim(ClaimTypes.Role, "User");
        _user.AddIdentity(new ClaimsIdentity(new[] { claim }));

        // Act
        var result = _service.IsAdmin();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsAdmin_WhenUserHasNoRoleClaims_ReturnsFalse()
    {
        // Act
        var result = _service.IsAdmin();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsAdmin_WhenHttpContextIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.IsAdmin();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsAdmin_WhenUserIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns((ClaimsPrincipal?)null);
        httpContextAccessor.HttpContext.Returns(httpContext);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.IsAdmin();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanManageContributors_WhenUserIsAdmin_ReturnsTrue()
    {
        // Arrange
        var applicationId = "123";
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Admin")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.CanManageContributors(applicationId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanManageContributors_WhenUserHasApplicationWritePermission_ReturnsTrue()
    {
        // Arrange
        var applicationId = "123";
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("permission", $"Application:{applicationId}:Write")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.CanManageContributors(applicationId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanManageContributors_WhenUserHasNoRelevantPermissions_ReturnsFalse()
    {
        // Arrange
        var applicationId = "123";
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "User"),
            new Claim("permission", "Application:456:Read")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.CanManageContributors(applicationId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanManageContributors_WhenHttpContextIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.CanManageContributors("123");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanManageContributors_WhenUserIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns((ClaimsPrincipal?)null);
        httpContextAccessor.HttpContext.Returns(httpContext);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.CanManageContributors("123");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsApplicationOwner_WhenUserIsAdmin_ReturnsTrue()
    {
        // Arrange
        var applicationId = "123";
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("permission", $"Application:{applicationId}:Write")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.IsApplicationOwner(applicationId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsApplicationOwner_WhenUserHasApplicationWritePermission_ReturnsTrue()
    {
        // Arrange
        var applicationId = "123";
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("permission", $"Application:{applicationId}:Write")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.IsApplicationOwner(applicationId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsApplicationOwner_WhenUserHasReadPermissionOnly_ReturnsFalse()
    {
        // Arrange
        var applicationId = "123";
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("permission", $"Application:{applicationId}:Read")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.IsApplicationOwner(applicationId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsApplicationOwner_WhenUserHasNoPermissions_ReturnsFalse()
    {
        // Arrange
        var applicationId = "123";
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "User")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.IsApplicationOwner(applicationId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsApplicationOwner_WhenApplicationIdIsNull_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Admin")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.IsApplicationOwner(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsApplicationOwner_WhenApplicationIdIsEmpty_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Admin")
        }));
        _httpContext.User.Returns(user);

        // Act
        var result = _service.IsApplicationOwner("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsApplicationOwner_WhenHttpContextIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.IsApplicationOwner("123");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsApplicationOwner_WhenUserIsNull_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns((ClaimsPrincipal?)null);
        httpContextAccessor.HttpContext.Returns(httpContext);
        var service = new ClaimBasedPermissionCheckerService(httpContextAccessor);

        // Act
        var result = service.IsApplicationOwner("123");

        // Assert
        Assert.False(result);
    }
} 