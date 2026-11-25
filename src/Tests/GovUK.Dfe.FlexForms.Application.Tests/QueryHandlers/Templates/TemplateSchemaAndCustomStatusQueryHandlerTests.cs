using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using MockQueryable.NSubstitute;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Templates;

/// <summary><c>GetTemplateSchemaByVersion</c> and <c>GetCustomApplicationStatusByApplicationStatus</c>.</summary>
public class TemplateSchemaAndCustomStatusQueryHandlerTests
{
    private static readonly Guid TemplateGuid = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly UserId CreatorId = new(Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"));
    private const string Email = "user@example.com";
    private const string ExternalId = "service-app-id";

    private readonly IEaRepository<User> _users = Substitute.For<IEaRepository<User>>();
    private readonly IEaRepository<TemplateVersion> _versions = Substitute.For<IEaRepository<TemplateVersion>>();
    private readonly IEaRepository<CustomApplicationStatus> _statuses = Substitute.For<IEaRepository<CustomApplicationStatus>>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantTemplateResolver _tenantTemplates = Substitute.For<ITenantTemplateResolver>();
    private readonly TemplateVersion _version = new(
        new TemplateVersionId(Guid.NewGuid()), new TemplateId(TemplateGuid), "1.2", """{"pages":[]}""", DateTime.UtcNow, CreatorId);

    public TemplateSchemaAndCustomStatusQueryHandlerTests()
    {
        var users = new List<User>
        {
            new(CreatorId, new RoleId(Guid.NewGuid()), "User", Email, DateTime.UtcNow, null, null, null, externalProviderId: ExternalId)
        }.BuildMockDbSet();
        _users.Query().Returns(users);
        var versions = new List<TemplateVersion> { _version }.BuildMockDbSet();
        _versions.Query().Returns(versions);
        _tenantTemplates.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateGuid), Arg.Any<CancellationToken>()).Returns(true);
        _permissions.HasPermission(ResourceType.Template, TemplateGuid.ToString(), AccessType.Read).Returns(true);
    }

    // ---- GetTemplateSchemaByVersion ----

    [Theory]
    [InlineData(ClaimTypes.Email, Email)]
    [InlineData("appid", ExternalId)]
    [InlineData("azp", ExternalId)]
    public async Task Schema_KnownCallerWithTemplateReadGetsTheVersion(string claimType, string value)
    {
        var result = await GetSchema(" 1.2 ", new Claim(claimType, value));

        Assert.True(result.IsSuccess);
        Assert.Equal(TemplateGuid, result.Value!.TemplateId);
        Assert.Equal(_version.Id!.Value, result.Value.TemplateVersionId);
        Assert.Equal("1.2", result.Value.VersionNumber);
        Assert.Equal("""{"pages":[]}""", result.Value.JsonSchema);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Schema_BlankVersionFailsValidation(string version)
    {
        var result = await GetSchema(version, new Claim(ClaimTypes.Email, Email));

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
    }

    [Fact]
    public async Task Schema_SignedOutCallerFails()
    {
        var result = await GetSchema("1.2", signedIn: false);

        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);
    }

    [Fact]
    public async Task Schema_CallerWithNoIdentifierIsForbidden()
    {
        var result = await GetSchema("1.2", new Claim(ClaimTypes.Name, "nobody"));

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        Assert.Equal("No user identifier", result.Error);
    }

    [Fact]
    public async Task Schema_TemplateInAnotherTenantIsForbidden()
    {
        _tenantTemplates.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateGuid), Arg.Any<CancellationToken>()).Returns(false);

        var result = await GetSchema("1.2", new Claim(ClaimTypes.Email, Email));

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Theory]
    [InlineData(ClaimTypes.Email, "stranger@example.com")]
    [InlineData("appid", "unknown-app")]
    public async Task Schema_UnknownUserIsNotFound(string claimType, string value)
    {
        var result = await GetSchema("1.2", new Claim(claimType, value));

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("User not found", result.Error);
    }

    [Fact]
    public async Task Schema_NoTemplateReadIsForbidden()
    {
        _permissions.HasPermission(ResourceType.Template, TemplateGuid.ToString(), AccessType.Read).Returns(false);

        var result = await GetSchema("1.2", new Claim(ClaimTypes.Email, Email));

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Schema_UnknownVersionIsNotFound()
    {
        var result = await GetSchema("9.9", new Claim(ClaimTypes.Email, Email));

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("Template version not found", result.Error);
    }

    [Fact]
    public async Task Schema_UnexpectedErrorFails()
    {
        _tenantTemplates.IsTemplateInCurrentTenantAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));

        var result = await GetSchema("1.2", new Claim(ClaimTypes.Email, Email));

        // The message carries the stack trace today; not asserted.
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.BadRequest, result.ErrorCode);
    }

    // ---- GetCustomApplicationStatusByApplicationStatus ----

    [Fact]
    public async Task CustomStatus_ReturnsTheTemplatesLabelForTheStatus()
    {
        var created = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var submitted = new CustomApplicationStatus(
            new CustomApplicationStatusId(Guid.NewGuid()), new TemplateId(TemplateGuid), ApplicationStatus.Submitted, "Sent to DfE", created, CreatorId);
        StoreStatuses(
            submitted,
            new CustomApplicationStatus(new CustomApplicationStatusId(Guid.NewGuid()), new TemplateId(TemplateGuid), ApplicationStatus.InProgress, "Drafting", created, CreatorId),
            new CustomApplicationStatus(new CustomApplicationStatusId(Guid.NewGuid()), new TemplateId(Guid.NewGuid()), ApplicationStatus.Submitted, "Other template", created, CreatorId));

        var result = await GetCustomStatus(ApplicationStatus.Submitted);

        Assert.Equal(submitted.Id!.Value, result.Value!.CustomApplicationStatusId);
        Assert.Equal(TemplateGuid, result.Value.TemplateId);
        Assert.Equal(ApplicationStatus.Submitted, result.Value.ApplicationStatus);
        Assert.Equal("Sent to DfE", result.Value.Label);
        Assert.Equal(created, result.Value.CreatedOn);
        Assert.Equal(CreatorId.Value, result.Value.CreatedBy);
    }

    [Fact]
    public async Task CustomStatus_TemplateInAnotherTenantIsNotFound()
    {
        _tenantTemplates.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateGuid), Arg.Any<CancellationToken>()).Returns(false);
        StoreStatuses();

        var result = await GetCustomStatus(ApplicationStatus.Submitted);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("Template not found", result.Error);
        _statuses.DidNotReceive().Query();
    }

    [Fact]
    public async Task CustomStatus_NoLabelForThatStatusIsNotFound()
    {
        StoreStatuses();

        var result = await GetCustomStatus(ApplicationStatus.Submitted);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("Custom application status not found", result.Error);
    }

    [Fact]
    public async Task CustomStatus_UnexpectedErrorFails()
    {
        _tenantTemplates.IsTemplateInCurrentTenantAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));

        var result = await GetCustomStatus(ApplicationStatus.Submitted);

        // The message carries the stack trace today; not asserted.
        Assert.Equal(DomainErrorCode.BadRequest, result.ErrorCode);
    }

    private void StoreStatuses(params CustomApplicationStatus[] statuses)
    {
        var set = statuses.ToList().BuildMockDbSet();
        _statuses.Query().Returns(set);
    }

    private Task<Result<CustomApplicationStatusDto>> GetCustomStatus(ApplicationStatus status) =>
        new GetCustomApplicationStatusByApplicationStatusQueryHandler(_statuses, _tenantTemplates)
            .Handle(new GetCustomApplicationStatusByApplicationStatusQuery(TemplateGuid, status), CancellationToken.None);

    private Task<Result<TemplateSchemaDto>> GetSchema(string version, Claim? claim = null, bool signedIn = true)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var identity = signedIn ? new ClaimsIdentity(claim is null ? [] : [claim], "Bearer") : new ClaimsIdentity();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
        return new GetTemplateSchemaByVersionQueryHandler(accessor, _users, _versions, _permissions, _tenantTemplates)
            .Handle(new GetTemplateSchemaByVersionQuery(TemplateGuid, version), CancellationToken.None);
    }
}
