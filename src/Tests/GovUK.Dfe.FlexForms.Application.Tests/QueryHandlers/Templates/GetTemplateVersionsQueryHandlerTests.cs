using AutoFixture;
using AutoFixture.Xunit2;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using Microsoft.AspNetCore.Http;
using MockQueryable;
using MockQueryable.NSubstitute;
using NSubstitute;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Templates;

public class GetTemplateVersionsQueryHandlerTests
{
    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldReturnVersionsNewestFirst_WhenUserHasPermission(
        Guid templateId,
        string emailName,
        UserCustomization userCustom,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IEaRepository<TemplateVersion> versionRepo,
        [Frozen] IPermissionCheckerService permissionCheckerService,
        [Frozen] ITenantTemplateResolver tenantTemplateResolver)
    {
        var email = $"{emailName}@example.com";
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Email, email)
        ], "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        userCustom.OverrideEmail = email;
        var user = new Fixture().Customize(userCustom).Create<User>();
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        tenantTemplateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(true);
        permissionCheckerService.HasPermission(ResourceType.Template, templateId.ToString(), AccessType.Read)
            .Returns(true);

        var older = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(templateId),
            "1.0.0",
            "{}",
            DateTime.UtcNow.AddDays(-2),
            user.Id!);
        var newer = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(templateId),
            "1.0.1",
            "{}",
            DateTime.UtcNow.AddDays(-1),
            user.Id!);

        versionRepo.Query().Returns(new List<TemplateVersion> { older, newer }.AsQueryable().BuildMock());

        var handler = new GetTemplateVersionsQueryHandler(
            httpContextAccessor,
            userRepo,
            versionRepo,
            permissionCheckerService,
            tenantTemplateResolver);

        var result = await handler.Handle(new GetTemplateVersionsQuery(templateId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("1.0.1", result.Value.First().VersionNumber);
        Assert.Equal("1.0.0", result.Value.Last().VersionNumber);
    }
}

public class GetTemplateSchemaByVersionQueryHandlerTests
{
    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldReturnSchema_WhenVersionExists(
        Guid templateId,
        string emailName,
        UserCustomization userCustom,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IEaRepository<TemplateVersion> versionRepo,
        [Frozen] IPermissionCheckerService permissionCheckerService,
        [Frozen] ITenantTemplateResolver tenantTemplateResolver)
    {
        var email = $"{emailName}@example.com";
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Email, email)
        ], "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        userCustom.OverrideEmail = email;
        var user = new Fixture().Customize(userCustom).Create<User>();
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        tenantTemplateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(true);
        permissionCheckerService.HasPermission(ResourceType.Template, templateId.ToString(), AccessType.Read)
            .Returns(true);

        const string versionNumber = "4.0.2";
        const string schema = "{\"templateVersion\":\"4.0.2\"}";
        var version = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(templateId),
            versionNumber,
            schema,
            DateTime.UtcNow,
            user.Id!);

        versionRepo.Query().Returns(new List<TemplateVersion> { version }.AsQueryable().BuildMock());

        var handler = new GetTemplateSchemaByVersionQueryHandler(
            httpContextAccessor,
            userRepo,
            versionRepo,
            permissionCheckerService,
            tenantTemplateResolver);

        var result = await handler.Handle(
            new GetTemplateSchemaByVersionQuery(templateId, versionNumber),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(versionNumber, result.Value!.VersionNumber);
        Assert.Equal(schema, result.Value.JsonSchema);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldReturnNotFound_WhenVersionMissing(
        Guid templateId,
        string emailName,
        UserCustomization userCustom,
        [Frozen] IHttpContextAccessor httpContextAccessor,
        [Frozen] IEaRepository<User> userRepo,
        [Frozen] IEaRepository<TemplateVersion> versionRepo,
        [Frozen] IPermissionCheckerService permissionCheckerService,
        [Frozen] ITenantTemplateResolver tenantTemplateResolver)
    {
        var email = $"{emailName}@example.com";
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Email, email)
        ], "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        userCustom.OverrideEmail = email;
        var user = new Fixture().Customize(userCustom).Create<User>();
        userRepo.Query().Returns(new List<User> { user }.AsQueryable().BuildMock());

        tenantTemplateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(true);
        permissionCheckerService.HasPermission(ResourceType.Template, templateId.ToString(), AccessType.Read)
            .Returns(true);
        versionRepo.Query().Returns(new List<TemplateVersion>().AsQueryable().BuildMock());

        var handler = new GetTemplateSchemaByVersionQueryHandler(
            httpContextAccessor,
            userRepo,
            versionRepo,
            permissionCheckerService,
            tenantTemplateResolver);

        var result = await handler.Handle(
            new GetTemplateSchemaByVersionQuery(templateId, "9.9.9"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }
}
