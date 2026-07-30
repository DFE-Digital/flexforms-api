using AutoFixture;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.TemplatePermissions.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using MediatR;
using GovUK.Dfe.CoreLibs.Testing.Helpers;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.TemplatePermissions;

public class GetTemplatePermissionsForUserByUserIdQueryObjectTests
{
    [Theory]
    [CustomAutoData(typeof(UserCustomization), typeof(PermissionCustomization))]
    public void Apply_ShouldReturnMatchingUser_WithAllTemplatePermissions(
        UserId userId,
        UserCustomization userCustom,
        PermissionCustomization permCustom)
    {
        // Arrange
        var sharedRoleId = new RoleId(Guid.NewGuid());
        userCustom.OverrideId = userId;
        userCustom.OverrideRoleId = sharedRoleId;
        userCustom.OverridePermissions = Array.Empty<Permission>();
        var fixtureUserA = new Fixture().Customize(userCustom);
        var userA = fixtureUserA.Create<User>();

        var backingField = typeof(User)
            .GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        backingField.SetValue(userA, new List<Permission>());

        permCustom.OverrideUserId = userId;
        permCustom.OverrideAppId = null;
        permCustom.OverrideResourceType = ResourceType.Template;
        permCustom.OverrideResourceKey = Guid.NewGuid().ToString();
        permCustom.OverrideAccessType = AccessType.Read;
        var tp = new Fixture().Customize(permCustom).Create<Permission>();
        ((List<Permission>)backingField.GetValue(userA)!).Add(tp);

        var userCustomB = new UserCustomization { OverrideRoleId = sharedRoleId };
        var userB = new Fixture().Customize(userCustomB).Create<User>();
        backingField.SetValue(userB, new List<Permission>());

        using var context = CreateAndSeedSqliteContext(ctx =>
        {
            ctx.Roles.Add(new Role(sharedRoleId, "TestRole"));
            ctx.Users.Add(userA);
            ctx.Users.Add(userB);
            ctx.SaveChanges();
        });

        // Act
        var queryable = context.Users.AsQueryable();
        var sut = new GetTemplatePermissionsForUserByUserIdQueryObject(userId);
        var result = sut.Apply(queryable).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(userId, result[0].Id);
        Assert.Single(result[0].Permissions.Where(p => p.ResourceType == ResourceType.Template));
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void Apply_ShouldReturnEmpty_WhenNoUserMatches(
        UserId userId,
        UserCustomization userCustom)
    {
        // Arrange
        var fixture = new Fixture().Customize(userCustom);
        var user = fixture.Create<User>();

        var backingField = typeof(User)
            .GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        backingField.SetValue(user, new List<Permission>());

        using var context = CreateAndSeedSqliteContext(ctx =>
        {
            ctx.Roles.Add(new Role(user.RoleId, "TestRole"));
            ctx.Users.Add(user);
            ctx.SaveChanges();
        });

        // Act
        var queryable = context.Users.AsQueryable();
        var sut = new GetTemplatePermissionsForUserByUserIdQueryObject(userId);
        var result = sut.Apply(queryable).ToList();

        // Assert
        Assert.Empty(result);
    }

    private ExternalApplicationsContext CreateAndSeedSqliteContext(Action<ExternalApplicationsContext> seed)
    {
        var services = new ServiceCollection();
        var dummyConfig = Substitute.For<IConfiguration>();
        services.AddSingleton<IConfiguration>(dummyConfig);
        var dummyMediator = Substitute.For<IMediator>();
        services.AddSingleton<IMediator>(dummyMediator);

        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using (var disable = connection.CreateCommand())
        {
            disable.CommandText = "PRAGMA foreign_keys = OFF;";
            disable.ExecuteNonQuery();
        }

        DbContextHelper.CreateDbContext<ExternalApplicationsContext>(services, connection, seed);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();

    }
}
