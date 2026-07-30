using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers
{
    public class UsersControllerTests
    {
        [Theory]
        [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
        public async Task GetAllMyPermissionsAsync_ShouldReturnPermissions_WhenUserExtAppIdExists(
            CustomWebApplicationDbContextFactory<Program> factory,
            IUsersClient usersClient,
            HttpClient httpClient)
        {
            var externalId = Guid.NewGuid();

            factory.TestClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "API.Read"),
                new Claim("iss", "windows.net"),
                new Claim("appid", externalId.ToString()),
                new Claim("permission", $"User:{externalId}:Read"),
            };

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "azure-token");

            // Arrange - use Bob to avoid clashing with WhenUserEmailExists which updates Alice to alice1@example.com
            var dbContext = factory.GetDbContext<ExternalApplicationsContext>();

            await dbContext.Users
                .Where(x => x.Email == EaContextSeeder.BobEmail)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.ExternalProviderId, externalId.ToString()));

            var bobId = dbContext.Users
                .Where(u => u.ExternalProviderId == externalId.ToString())
                .Select(u => u.Id)
                .Single();

            await dbContext.Permissions
                .Where(p => p.UserId == bobId)
                .ExecuteUpdateAsync(p => p
                    .SetProperty(p => p.ResourceKey, externalId.ToString())
                    .SetProperty(p => p.ResourceType, ResourceType.User)
                    .SetProperty(p => p.AccessType, AccessType.Read)
                );

            var expectedUser = dbContext.Users
                .Include(u => u.Permissions)
                .Include(u => u.Role)
                .FirstOrDefault(u => u.ExternalProviderId == externalId.ToString())!;

            var expectedPermissions = expectedUser.Permissions
                .Select(p => new UserPermissionDto
                {
                    ApplicationId = p.ApplicationId?.Value,
                    ResourceType = p.ResourceType,
                    ResourceKey = p.ResourceKey,
                    AccessType = p.AccessType
                })
                .ToList();

            var result = await usersClient.GetMyPermissionsAsync();

            // Assert: Check permissions
            Assert.NotNull(result);
            Assert.NotNull(result.Permissions);
            Assert.Equal(expectedPermissions.Count, result.Permissions.Count());

            var actual = result.Permissions
                .Select(dto => (dto.ResourceType, dto.ResourceKey, dto.AccessType))
                .OrderBy(x => x.ResourceType)
                .ThenBy(x => x.ResourceKey)
                .ThenBy(x => x.AccessType)
                .ToList();

            var expected = expectedPermissions
                .Select(dto => (dto.ResourceType, dto.ResourceKey, dto.AccessType))
                .OrderBy(x => x.ResourceType)
                .ThenBy(x => x.ResourceKey)
                .ThenBy(x => x.AccessType)
                .ToList();

            // Compare element by element
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].ResourceType, actual[i].ResourceType);
                Assert.Equal(expected[i].ResourceKey, actual[i].ResourceKey);
                Assert.Equal(expected[i].AccessType, actual[i].AccessType);
            }

            // Assert: Check roles
            Assert.NotNull(result.Roles);
            if (expectedUser.Role != null)
            {
                Assert.Single(result.Roles);
                Assert.Equal(expectedUser.Role.Name, result.Roles.First());
            }
            else
            {
                Assert.Empty(result.Roles);
            }
        }

        [Theory]
        [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
        public async Task GetAllMyPermissionsAsync_ShouldReturnPermissions_WhenUserEmailExists(
         CustomWebApplicationDbContextFactory<Program> factory,
         IUsersClient usersClient,
         HttpClient httpClient)
        {
            // Use a new user so GetAllUserPermissionsQueryHandler cache is cold (no other test requests this user's permissions)
            var testEmail = $"email-test-{Guid.NewGuid()}@example.com";
            factory.TestClaims = new List<Claim>
            {
                new Claim("permission", $"User:{testEmail}:Read"),
                new Claim(ClaimTypes.Email, testEmail),
            };

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "user-token");

            // Arrange - create a dedicated user and permission so cache returns fresh data
            var dbContext = factory.GetDbContext<ExternalApplicationsContext>();
            var submitterRoleId = dbContext.Roles.Where(r => r.Name == "Submitter").Select(r => r.Id).Single();
            var aliceUserId = new GovUK.Dfe.FlexForms.Domain.ValueObjects.UserId(new Guid(EaContextSeeder.AliceId));

            var newUserId = new GovUK.Dfe.FlexForms.Domain.ValueObjects.UserId(Guid.NewGuid());
            var newUser = new GovUK.Dfe.FlexForms.Domain.Entities.User(
                newUserId,
                submitterRoleId,
                name: "Email Test User",
                email: testEmail,
                createdOn: DateTime.UtcNow,
                createdBy: null,
                lastModifiedOn: null,
                lastModifiedBy: null,
                externalProviderId: null,
                initialPermissions: null);
            dbContext.Users.Add(newUser);

            var permId = new GovUK.Dfe.FlexForms.Domain.ValueObjects.PermissionId(Guid.NewGuid());
            var perm = new GovUK.Dfe.FlexForms.Domain.Entities.Permission(
                permId,
                newUserId,
                applicationId: null,
                resourceKey: testEmail,
                resourceType: ResourceType.User,
                accessType: AccessType.Read,
                grantedOn: DateTime.UtcNow,
                grantedBy: aliceUserId);
            dbContext.Permissions.Add(perm);
            await dbContext.SaveChangesAsync();

            var expectedUser = dbContext.Users
                .Include(u => u.Permissions)
                .Include(u => u.Role)
                .First(u => u.Email == testEmail);

            var expectedPermissions = expectedUser.Permissions
                .Select(p => new UserPermissionDto
                {
                    ApplicationId = p.ApplicationId?.Value,
                    ResourceType = p.ResourceType,
                    ResourceKey = p.ResourceKey,
                    AccessType = p.AccessType
                })
                .ToList();

            var result = await usersClient.GetMyPermissionsAsync();

            // Assert: Check permissions
            Assert.NotNull(result);
            Assert.NotNull(result.Permissions);
            Assert.Equal(expectedPermissions.Count, result.Permissions.Count());

            var actual = result.Permissions
                .Select(dto => (dto.ResourceType, dto.ResourceKey, dto.AccessType))
                .OrderBy(x => x.ResourceType)
                .ThenBy(x => x.ResourceKey)
                .ThenBy(x => x.AccessType)
                .ToList();

            var expected = expectedPermissions
                .Select(dto => (dto.ResourceType, dto.ResourceKey, dto.AccessType))
                .OrderBy(x => x.ResourceType)
                .ThenBy(x => x.ResourceKey)
                .ThenBy(x => x.AccessType)
                .ToList();

            // Compare element by element
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].ResourceType, actual[i].ResourceType);
                Assert.Equal(expected[i].ResourceKey, actual[i].ResourceKey);
                Assert.Equal(expected[i].AccessType, actual[i].AccessType);
            }

            // Assert: Check roles
            Assert.NotNull(result.Roles);
            if (expectedUser.Role != null)
            {
                Assert.Single(result.Roles);
                Assert.Equal(expectedUser.Role.Name, result.Roles.First());
            }
            else
            {
                Assert.Empty(result.Roles);
            }
        }

        [Theory]
        [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
        public async Task GetMyPermissionsAsync_ShouldReturnUnauthorized_WhenTokenMissing(
     CustomWebApplicationDbContextFactory<Program> factory,
     IUsersClient usersClient)
        {
            var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
                () => usersClient.GetMyPermissionsAsync());

            Assert.Equal(401, ex.StatusCode);
        }

        [Theory]
        [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
        public async Task GetMyPermissionsAsync_ShouldReturnNotFound_WhenUserDoesNotExist(
            CustomWebApplicationDbContextFactory<Program> factory,
            IUsersClient usersClient,
            HttpClient httpClient)
        {
            factory.TestClaims =
            [
                new Claim(ClaimTypes.Email, $"unknown-{Guid.NewGuid()}@example.com")
            ];

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "user-token");

            var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
                () => usersClient.GetMyPermissionsAsync());

            Assert.Equal(404, ex.StatusCode);
        }

        [Theory]
        [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
        public async Task GetMyApplicationsAsync_ShouldReturnApplications_WhenUserEmailExists(
          CustomWebApplicationDbContextFactory<Program> factory,
          IApplicationsClient appsClient,
          HttpClient httpClient)
        {
            factory.TestClaims = new List<Claim>
            {
                new Claim("permission", $"Application:{EaContextSeeder.ApplicationId}:Read"),
                new Claim(ClaimTypes.Email, "alice@example.com")
            };

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "user-token");

            var list = await appsClient.GetMyApplicationsAsync();

            Assert.NotNull(list);
            Assert.NotEmpty(list!.Items);
        }

        [Theory]
        [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
        public async Task GetApplicationsForUserAsync_ShouldReturnApplications_WhenAuthorized(
            CustomWebApplicationDbContextFactory<Program> factory,
            IApplicationsClient appsClient,
            HttpClient httpClient)
        {
            factory.TestClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "API.Read"),
                new Claim("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
            };

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "azure-token");

            var list = await appsClient.GetApplicationsForUserAsync("bob@example.com");

            Assert.NotNull(list);
            Assert.NotEmpty(list!.Items);
        }

        [Theory]
        [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
        public async Task GetMyApplicationsAsync_ShouldReturnUnauthorized_WhenTokenMissing(
            CustomWebApplicationDbContextFactory<Program> factory,
            IApplicationsClient appsClient
            )
        {
            var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
                () => appsClient.GetMyApplicationsAsync(includeSchema: null));
            Assert.Equal(403, ex.StatusCode);
        }

        [Theory]
        [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
        public async Task GetApplicationsForUserAsync_ShouldReturnForbidden_WhenPermissionMissing(
            CustomWebApplicationDbContextFactory<Program> factory,
            IApplicationsClient appsClient,
            HttpClient httpClient)
        {
            factory.TestClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "API.Read"),
            };

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "azure-token");

            var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
                () => appsClient.GetApplicationsForUserAsync("bob@example.com", includeSchema: null));

            Assert.Equal(403, ex.StatusCode);
        }
    }
}
