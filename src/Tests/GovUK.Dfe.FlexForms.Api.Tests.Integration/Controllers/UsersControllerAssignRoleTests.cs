using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

public class UsersControllerAssignRoleTests
{
    private const string AdminEmail = "alice@example.com";

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AssignUserRoleAsync_ShouldCreateUser_WhenAdminAndUserDoesNotExist(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        ConfigureAdminCaller(factory, httpClient);

        var email = $"user-{Guid.NewGuid()}@example.com";
        var request = new AssignUserRoleRequest
        {
            Email = email,
            Name = "New User",
            Role = RoleNames.User,
            TemplateIds = [Guid.Parse(EaContextSeeder.TemplateId)]
        };

        var result = await usersClient.AssignUserRoleAsync(request);

        Assert.NotNull(result);
        Assert.Equal(email, result.Email);
        Assert.Equal("New User", result.Name);
        Assert.NotNull(result.Authorization);
        Assert.Contains(RoleNames.User, result.Authorization!.Roles!);

        var dbContext = factory.GetDbContext<ExternalApplicationsContext>();
        var createdUser = await dbContext.Users
            .Include(u => u.Role)
            .Include(u => u.Permissions)
            .Include(u => u.TemplatePermissions)
            .SingleAsync(u => u.Email == email);

        Assert.Equal(RoleNames.User, createdUser.Role!.Name);
        Assert.Contains(
            createdUser.TemplatePermissions,
            tp => tp.TemplateId.Value == Guid.Parse(EaContextSeeder.TemplateId)
                  && tp.AccessType == AccessType.Read);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AssignUserRoleAsync_ShouldReturnBadRequest_WhenRoleIsCaseworker(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        ConfigureAdminCaller(factory, httpClient);

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.AssignUserRoleAsync(new AssignUserRoleRequest
            {
                Email = $"caseworker-{Guid.NewGuid()}@example.com",
                Name = "Legacy Caseworker",
                Role = RoleNames.Caseworker,
                TemplateIds = [Guid.Parse(EaContextSeeder.TemplateId)]
            }));

        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AssignUserRoleAsync_ShouldReturnForbidden_WhenCallerIsNotAdmin(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        factory.TestClaims =
        [
            new(ClaimTypes.Email, EaContextSeeder.BobEmail),
            new("permission", $"Application:{EaContextSeeder.ApplicationId}:Read")
        ];

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.AssignUserRoleAsync(new AssignUserRoleRequest
            {
                Email = $"forbidden-{Guid.NewGuid()}@example.com",
                Name = "Test User",
                Role = RoleNames.User,
                TemplateIds = [Guid.Parse(EaContextSeeder.TemplateId)]
            }));

        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AssignUserRoleAsync_ShouldReturnForbidden_WhenTokenMissing(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient)
    {
        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.AssignUserRoleAsync(new AssignUserRoleRequest
            {
                Email = $"missing-token-{Guid.NewGuid()}@example.com",
                Name = "Test User",
                Role = RoleNames.User,
                TemplateIds = [Guid.Parse(EaContextSeeder.TemplateId)]
            }));

        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AssignUserRoleAsync_ShouldReturnBadRequest_WhenCustomRoleDoesNotExist(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        ConfigureAdminCaller(factory, httpClient);

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.AssignUserRoleAsync(new AssignUserRoleRequest
            {
                Email = $"invalid-role-{Guid.NewGuid()}@example.com",
                Name = "Test User",
                Role = "SuperUser",
                TemplateIds = [Guid.Parse(EaContextSeeder.TemplateId)]
            }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("not found", ex.Result?.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task AssignUserRoleAsync_ShouldReturnBadRequest_WhenTemplateIdsMissingForUser(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUsersClient usersClient,
        HttpClient httpClient)
    {
        ConfigureAdminCaller(factory, httpClient);

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(
            () => usersClient.AssignUserRoleAsync(new AssignUserRoleRequest
            {
                Email = $"missing-template-{Guid.NewGuid()}@example.com",
                Name = "Test User",
                Role = RoleNames.User,
                TemplateIds = null
            }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("template ID", ex.Result?.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static void ConfigureAdminCaller(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient httpClient)
    {
        factory.TestClaims =
        [
            new(ClaimTypes.Email, AdminEmail),
            new(ClaimTypes.Role, RoleNames.Admin)
        ];

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "user-token");
    }
}
