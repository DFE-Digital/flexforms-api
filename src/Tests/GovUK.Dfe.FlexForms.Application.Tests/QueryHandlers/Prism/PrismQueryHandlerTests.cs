using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Identifiers;
using GovUK.Dfe.FlexForms.Application.Prism.Queries;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Prism;

public class PrismQueryHandlerTests
{
    private static readonly UserId User = new(Guid.NewGuid());
    private readonly TemplateId _templateId = new(Guid.NewGuid());
    private readonly IEaRepository<Domain.Entities.Application> _applicationRepo = Substitute.For<IEaRepository<Domain.Entities.Application>>();
    private readonly ITenantTemplateResolver _templateResolver = Substitute.For<ITenantTemplateResolver>();

    public PrismQueryHandlerTests()
    {
        _templateResolver.IsTemplateInCurrentTenantAsync(_templateId, Arg.Any<CancellationToken>()).Returns(true);
        _templateResolver.GetTemplateIdsForCurrentTenantAsync(Arg.Any<CancellationToken>()).Returns([_templateId]);
    }

    private Domain.Entities.Application NewApplication(DateTime? createdOn = null, TemplateId? templateId = null)
    {
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            new ApplicationId(Guid.NewGuid()), $"APP-{Guid.NewGuid():N}"[..12], templateVersionId, createdOn ?? DateTime.UtcNow, User);
        var templateVersion = new TemplateVersion(templateVersionId, templateId ?? _templateId, "1.0", "{}", DateTime.UtcNow, User);
        typeof(Domain.Entities.Application).GetProperty(nameof(Domain.Entities.Application.TemplateVersion))!.SetValue(application, templateVersion);
        return application;
    }

    private static ApplicationResponse AddResponse(Domain.Entities.Application application, string body)
    {
        var response = new ApplicationResponse(new ResponseId(Guid.NewGuid()), application.Id!, body, DateTime.UtcNow, User);
        application.AddResponse(response);
        return response;
    }

    private void Seed(params Domain.Entities.Application[] applications)
    {
        var dbSet = applications.AsQueryable().BuildMockDbSet();
        _applicationRepo.Query().Returns(dbSet);
    }

    [Fact]
    public async Task Tenants_returns_every_configured_tenant()
    {
        var provider = Substitute.For<ITenantConfigurationProvider>();
        var settings = new ConfigurationBuilder().Build();
        provider.GetAllTenants().Returns([
            new TenantConfiguration(Guid.NewGuid(), "Beta", settings, []),
            new TenantConfiguration(Guid.NewGuid(), "Alpha", settings, [])
        ]);

        var result = await new GetPrismTenantsQueryHandler(provider).Handle(new GetPrismTenantsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Alpha", "Beta"], result.Value!.Select(t => t.TenantName));
    }

    [Fact]
    public async Task Current_returns_latest_response_and_revision()
    {
        var application = NewApplication();
        AddResponse(application, "{\"v\":1}");
        var latest = AddResponse(application, "{\"v\":2}");
        Seed(application);

        var result = await new GetPrismApplicationStateQueryHandler(_applicationRepo, _templateResolver)
            .Handle(new GetPrismApplicationStateQuery(application.Id!.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal(2, dto.SourceRevision);
        Assert.Equal(latest.Id!.Value, dto.ResponseId);
        Assert.Equal(2, dto.ResponseRevision);
        Assert.Equal("{\"v\":2}", dto.ResponseBody);
        Assert.False(dto.IsDeleted);
        Assert.Null(dto.SubmissionId);
    }

    [Fact]
    public async Task Current_reports_submission_and_deletion()
    {
        var application = NewApplication();
        var submitted = AddResponse(application, "{}");
        application.Submit(DateTime.UtcNow, User, "u@example.com", "U");
        application.Delete(DateTime.UtcNow, User, "u@example.com", "U");
        Seed(application);

        var result = await new GetPrismApplicationStateQueryHandler(_applicationRepo, _templateResolver)
            .Handle(new GetPrismApplicationStateQuery(application.Id!.Value), CancellationToken.None);

        var dto = result.Value!;
        Assert.True(dto.IsDeleted);
        Assert.Equal(ApplicationStatus.Deleted, dto.Status);
        Assert.Equal(3, dto.SourceRevision);
        Assert.Equal(2, dto.SubmittedRevision);
        Assert.Equal(ApplicationProjectionIdentifiers.SubmissionId(application.Id!.Value, 2), dto.SubmissionId);
        Assert.Equal(submitted.Id!.Value, dto.SubmittedResponseId);
    }

    [Fact]
    public async Task Current_returns_not_found_for_another_tenants_application()
    {
        var application = NewApplication(templateId: new TemplateId(Guid.NewGuid()));
        AddResponse(application, "{}");
        Seed(application);

        var result = await new GetPrismApplicationStateQueryHandler(_applicationRepo, _templateResolver)
            .Handle(new GetPrismApplicationStateQuery(application.Id!.Value), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Response_returns_exact_version()
    {
        var application = NewApplication();
        var first = AddResponse(application, "{\"v\":1}");
        AddResponse(application, "{\"v\":2}");
        Seed(application);

        var result = await new GetPrismResponseQueryHandler(_applicationRepo, _templateResolver)
            .Handle(new GetPrismResponseQuery(first.Id!.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedAtRevision);
        Assert.Equal("{\"v\":1}", result.Value.ResponseBody);
    }

    [Fact]
    public async Task Response_returns_not_found_for_another_tenant()
    {
        var application = NewApplication(templateId: new TemplateId(Guid.NewGuid()));
        var response = AddResponse(application, "{}");
        Seed(application);

        var result = await new GetPrismResponseQueryHandler(_applicationRepo, _templateResolver)
            .Handle(new GetPrismResponseQuery(response.Id!.Value), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Template_version_returns_schema_for_tenant_template()
    {
        var versionId = new TemplateVersionId(Guid.NewGuid());
        var repo = Substitute.For<IEaRepository<TemplateVersion>>();
        var dbSet = new[] { new TemplateVersion(versionId, _templateId, "2.0", "{\"schema\":true}", DateTime.UtcNow, User) }
            .AsQueryable().BuildMockDbSet();
        repo.Query().Returns(dbSet);

        var result = await new GetPrismTemplateVersionQueryHandler(repo, _templateResolver)
            .Handle(new GetPrismTemplateVersionQuery(versionId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("{\"schema\":true}", result.Value!.JsonSchema);
        Assert.Equal(_templateId.Value, result.Value.TemplateId);
    }

    [Fact]
    public async Task List_includes_deleted_and_pages_oldest_first()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a1 = NewApplication(t0);
        var a2 = NewApplication(t0.AddDays(1));
        var a3 = NewApplication(t0.AddDays(2));
        AddResponse(a2, "{}");
        a2.Delete(t0.AddDays(3), User, "u@example.com", "U");
        var otherTenant = NewApplication(t0, new TemplateId(Guid.NewGuid()));
        Seed(a3, otherTenant, a2, a1);

        var handler = new ListPrismApplicationsQueryHandler(_applicationRepo, _templateResolver);
        var page1 = await handler.Handle(new ListPrismApplicationsQuery(null, 1, 2), CancellationToken.None);
        var page2 = await handler.Handle(new ListPrismApplicationsQuery(null, 2, 2), CancellationToken.None);

        Assert.Equal([a1.Id!.Value, a2.Id!.Value], page1.Value!.Items.Select(i => i.ApplicationId));
        Assert.True(page1.Value.HasMore);
        Assert.True(page1.Value.Items[1].IsDeleted);
        Assert.Equal(2, page1.Value.Items[1].SourceRevision);
        Assert.Equal([a3.Id!.Value], page2.Value!.Items.Select(i => i.ApplicationId));
        Assert.False(page2.Value.HasMore);
    }

    [Fact]
    public async Task List_filters_by_modified_since()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var old = NewApplication(t0);
        var recent = NewApplication(t0.AddDays(10));
        Seed(old, recent);

        var result = await new ListPrismApplicationsQueryHandler(_applicationRepo, _templateResolver)
            .Handle(new ListPrismApplicationsQuery(t0.AddDays(5), 1, 100), CancellationToken.None);

        Assert.Equal([recent.Id!.Value], result.Value!.Items.Select(i => i.ApplicationId));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, ListPrismApplicationsQueryHandler.MaxPageSize + 1)]
    public async Task List_validates_paging(int page, int pageSize)
    {
        var result = await new ListPrismApplicationsQueryHandler(_applicationRepo, _templateResolver)
            .Handle(new ListPrismApplicationsQuery(null, page, pageSize), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
    }
}
