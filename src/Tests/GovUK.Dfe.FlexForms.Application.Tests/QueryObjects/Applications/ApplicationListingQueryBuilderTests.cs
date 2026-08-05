using AutoFixture;
using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using MockQueryable;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Applications;

public class ApplicationListingQueryBuilderTests
{
    [Fact]
    public async Task MapPagedResultAsync_ShouldReturnNewestApplicationsOnPageOne()
    {
        var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldest = CreateApplication(baseDate);
        var middle = CreateApplication(baseDate.AddDays(1));
        var newest = CreateApplication(baseDate.AddDays(2));

        var query = new List<Domain.Entities.Application> { oldest, middle, newest }.AsQueryable().BuildMock();

        var applicationRepository = Substitute.For<IApplicationRepository>();
        applicationRepository
            .GetLatestResponsesAsync(Arg.Any<IReadOnlyCollection<ApplicationId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<ApplicationId, ApplicationResponse>());

        var pageOne = await ApplicationListingQueryBuilder.MapPagedResultAsync(
            query,
            includeSchema: false,
            pageNumber: 1,
            pageSize: 1,
            applicationRepository,
            CancellationToken.None);

        var pageTwo = await ApplicationListingQueryBuilder.MapPagedResultAsync(
            query,
            includeSchema: false,
            pageNumber: 2,
            pageSize: 1,
            applicationRepository,
            CancellationToken.None);

        Assert.Equal(newest.ApplicationReference, pageOne.Items.Single().ApplicationReference);
        Assert.Equal(middle.ApplicationReference, pageTwo.Items.Single().ApplicationReference);
    }

    private static Domain.Entities.Application CreateApplication(DateTime createdOn)
    {
        var fixture = new Fixture().Customize(new ApplicationCustomization { OverrideCreatedOn = createdOn });
        return fixture.Create<Domain.Entities.Application>();
    }

    [Fact]
    public void ApplicationAccessResolver_ReturnsAllApplicationsInTenant_ForAdmin_ButMyApplicationsQueryIgnoresRole()
    {
        var userId = new UserId(Guid.NewGuid());
        var admin = new User(
            userId,
            new RoleId(RoleConstants.AdminRoleId),
            "Admin",
            "admin@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        admin.GetType().GetProperty(nameof(User.Role))!.SetValue(admin,
            new Role(new RoleId(RoleConstants.AdminRoleId), RoleNames.Admin));

        var scope = ApplicationAccessResolver.Resolve(admin);
        Assert.Equal(ApplicationAccessResolver.AccessMode.AllApplicationsInTenant, scope.Mode);

        var permissions = admin.GetType()
            .GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        permissions.SetValue(admin, new List<Permission>());

        var appRepo = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        appRepo.Query().Returns(new List<Domain.Entities.Application>().AsQueryable());

        var query = ApplicationListingQueryBuilder.BuildMyApplicationsQuery(appRepo, admin, Array.Empty<TemplateId>());

        Assert.NotNull(query);
    }
}
