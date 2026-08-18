using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Application.Tests.Helpers;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using MockQueryable;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Applications;

public class GetApplicationsByTemplateQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnApplications_WhenCallerHasApplicationAnyReadClaim()
    {
        var email = "caseworker@example.com";
        var templateId = new TemplateId(Guid.NewGuid());
        var user = CreateStandardUser(email);
        var application = CreateApplication(user, templateId);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, email), new Claim("permission", "Application:Any:Read")],
                "TestAuth"))
        };
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var userRepo = Substitute.For<IEaRepository<User>>();
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        var appRepo = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        appRepo.Query().Returns(new List<Domain.Entities.Application> { application }.AsQueryable().BuildMock());

        var permissionChecker = Substitute.For<IPermissionCheckerService>();
        permissionChecker.CanReadAllApplications().Returns(true);

        var handler = ApplicationListingTestHelper.CreateGetApplicationsByTemplateQueryHandler(
            httpContextAccessor,
            userRepo,
            appRepo,
            Substitute.For<ITenantContextAccessor>(),
            ApplicationListingTestHelper.CreateTemplateResolver(templateId),
            permissionChecker);

        var result = await handler.Handle(
            new GetApplicationsByTemplateQuery(templateId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(application.Id!.Value, result.Value.Items.First().ApplicationId);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerCannotListAllApplications()
    {
        var email = "user@example.com";
        var templateId = new TemplateId(Guid.NewGuid());
        var user = CreateStandardUser(email);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, email)],
                "TestAuth"))
        };
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var userRepo = Substitute.For<IEaRepository<User>>();
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        var permissionChecker = Substitute.For<IPermissionCheckerService>();
        permissionChecker.CanReadAllApplications().Returns(false);

        var handler = ApplicationListingTestHelper.CreateGetApplicationsByTemplateQueryHandler(
            httpContextAccessor,
            userRepo,
            Substitute.For<IEaRepository<Domain.Entities.Application>>(),
            Substitute.For<ITenantContextAccessor>(),
            ApplicationListingTestHelper.CreateTemplateResolver(templateId),
            permissionChecker);

        var result = await handler.Handle(
            new GetApplicationsByTemplateQuery(templateId.Value),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    private static User CreateStandardUser(string email)
    {
        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            "Caseworker",
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        user.GetType().GetProperty(nameof(User.Role))!.SetValue(
            user,
            new Role(new RoleId(RoleConstants.UserRoleId), RoleNames.User));

        return user;
    }

    private static Domain.Entities.Application CreateApplication(User user, TemplateId templateId)
    {
        var application = new Domain.Entities.Application(
            new ApplicationId(Guid.NewGuid()),
            "REF-1",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        ApplicationListingTestHelper.AttachTemplateVersion(application, templateId, user.Id!);
        return application;
    }
}
