using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Application.Tests.Helpers;

internal static class ApplicationListingTestHelper
{
    internal static IUserAccessibleTemplateService CreateEmptyAccessibleTemplateService()
    {
        var service = Substitute.For<IUserAccessibleTemplateService>();
        service.ResolveAccessibleListingFilterAsync(
                Arg.Any<IEnumerable<Permission>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TemplateId>());
        return service;
    }

    internal static IUserAccessibleTemplateService CreateAccessibleTemplateService(params TemplateId[] allowedTemplateIds)
    {
        var allowed = allowedTemplateIds.ToHashSet();
        var service = Substitute.For<IUserAccessibleTemplateService>();
        service.ResolveAccessibleListingFilterAsync(
                Arg.Any<IEnumerable<Permission>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var requested = call.Arg<Guid?>();
                if (requested.HasValue)
                {
                    var templateId = new TemplateId(requested.Value);
                    return allowed.Contains(templateId)
                        ? new[] { templateId }
                        : Array.Empty<TemplateId>();
                }

                return allowed.ToList().AsReadOnly();
            });
        return service;
    }

    internal static ITenantTemplateResolver CreateTemplateResolver(params TemplateId[] allowedTemplateIds)
    {
        var allowed = allowedTemplateIds.ToHashSet();
        var resolver = Substitute.For<ITenantTemplateResolver>();
        resolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(call => allowed.Contains(call.Arg<TemplateId>()));
        resolver.ResolveListingTemplateFilterAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var requested = call.Arg<Guid?>();
                if (requested.HasValue)
                {
                    var templateId = new TemplateId(requested.Value);
                    return allowed.Contains(templateId)
                        ? new[] { templateId }
                        : Array.Empty<TemplateId>();
                }

                return allowed.ToList().AsReadOnly();
            });
        return resolver;
    }

    internal static void AttachTemplateVersion(
        Domain.Entities.Application application,
        TemplateId templateId,
        UserId createdBy,
        Guid? ownerTenantId = null)
    {
        var template = new Template(templateId, "Test Template", DateTime.UtcNow, createdBy, tenantId: ownerTenantId);
        var templateVersion = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            templateId,
            "1.0",
            "{}",
            DateTime.UtcNow,
            createdBy);
        templateVersion.GetType().GetProperty(nameof(TemplateVersion.Template))?.SetValue(templateVersion, template);
        application.GetType().GetProperty(nameof(Domain.Entities.Application.TemplateVersion))
            ?.SetValue(application, templateVersion);
    }

    internal static void ConfigurePassthroughCache(
        ICacheService<IRedisCacheType> cache,
        string methodName)
    {
        cache.GetOrAddAsync(
                Arg.Any<string>(),
                Arg.Any<Func<Task<Result<PagedResult<ApplicationDto>>>>>(),
                methodName)
            .Returns(call => call.Arg<Func<Task<Result<PagedResult<ApplicationDto>>>>>()());
    }

    internal static IApplicationRepository CreateApplicationRepository()
    {
        var repository = Substitute.For<IApplicationRepository>();
        repository
            .GetLatestResponsesAsync(Arg.Any<IReadOnlyCollection<ApplicationId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<ApplicationId, ApplicationResponse>());
        return repository;
    }

    internal static GetApplicationsForUserQueryHandler CreateGetApplicationsForUserQueryHandler(
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IEaRepository<Domain.Entities.Application> appRepo,
        ITenantContextAccessor tenantContextAccessor,
        IUserAccessibleTemplateService accessibleTemplateService,
        ICacheService<IRedisCacheType>? cache = null)
    {
        cache ??= Substitute.For<ICacheService<IRedisCacheType>>();
        ConfigurePassthroughCache(cache, nameof(GetApplicationsForUserQueryHandler));

        return new GetApplicationsForUserQueryHandler(
            userRepo,
            appRepo,
            permissionCheckerService,
            CreateApplicationRepository(),
            cache,
            tenantContextAccessor,
            accessibleTemplateService,
            Substitute.For<ILogger<GetApplicationsForUserQueryHandler>>());
    }

    internal static GetApplicationsForUserByExternalProviderIdQueryHandler CreateGetApplicationsForUserByExternalProviderIdQueryHandler(
        IEaRepository<User> userRepo,
        IEaRepository<Domain.Entities.Application> appRepo,
        IUserAccessibleTemplateService accessibleTemplateService,
        ICacheService<IRedisCacheType>? cache = null,
        ITenantContextAccessor? tenantContextAccessor = null)
    {
        cache ??= Substitute.For<ICacheService<IRedisCacheType>>();
        ConfigurePassthroughCache(cache, nameof(GetApplicationsForUserByExternalProviderIdQueryHandler));
        tenantContextAccessor ??= Substitute.For<ITenantContextAccessor>();

        return new GetApplicationsForUserByExternalProviderIdQueryHandler(
            userRepo,
            appRepo,
            CreateApplicationRepository(),
            cache,
            tenantContextAccessor,
            accessibleTemplateService);
    }

    internal static GetApplicationsByTemplateQueryHandler CreateGetApplicationsByTemplateQueryHandler(
        IHttpContextAccessor httpContextAccessor,
        IEaRepository<User> userRepo,
        IEaRepository<Domain.Entities.Application> appRepo,
        ITenantContextAccessor tenantContextAccessor,
        ITenantTemplateResolver tenantTemplateResolver,
        IPermissionCheckerService permissionCheckerService,
        ICacheService<IRedisCacheType>? cache = null)
    {
        cache ??= Substitute.For<ICacheService<IRedisCacheType>>();
        ConfigurePassthroughCache(cache, nameof(GetApplicationsByTemplateQueryHandler));

        return new GetApplicationsByTemplateQueryHandler(
            httpContextAccessor,
            userRepo,
            appRepo,
            permissionCheckerService,
            CreateApplicationRepository(),
            cache,
            tenantContextAccessor,
            tenantTemplateResolver);
    }
}
