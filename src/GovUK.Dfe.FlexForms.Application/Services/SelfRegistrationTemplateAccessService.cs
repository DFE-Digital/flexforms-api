using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <inheritdoc />
public sealed class SelfRegistrationTemplateAccessService(
    IEaRepository<Template> templateRepo,
    ITenantTemplateResolver tenantTemplateResolver,
    ITenantContextAccessor tenantContextAccessor,
    IUserFactory userFactory) : ISelfRegistrationTemplateAccessService
{
    public const string DefaultTemplateIdKey = "SelfRegistration:DefaultTemplateId";
    public const string LegacyDefaultTemplateIdKey = "ExternalApplicationsApiClient:DefaultTemplateId";

    /// <inheritdoc />
    public async Task<bool> EnsureLiveTemplateAccessAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Id is null)
            return false;

        var liveTemplates = await GetLiveTemplatesForCurrentTenantAsync(cancellationToken);
        var liveIds = liveTemplates
            .Where(t => t.Id is not null)
            .Select(t => t.Id!)
            .ToList();

        var toGrant = SelfRegistrationAccessRules.ResolveAutoGrantedTemplates(
            liveIds,
            ReadDefaultTemplateId());
        if (toGrant.Count == 0)
            return false;

        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var templateId in toGrant)
        {
            if (SelfRegistrationAccessRules.HasTemplateAccess(user, templateId))
                continue;

            userFactory.EnsureUserHasTemplatePermission(user, templateId, user.Id, now);
            changed = true;
        }

        return changed;
    }

    private TemplateId? ReadDefaultTemplateId()
    {
        var settings = tenantContextAccessor.CurrentTenant?.Settings;
        if (settings is null)
            return null;

        var raw = settings[DefaultTemplateIdKey] ?? settings[LegacyDefaultTemplateIdKey];
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var guid) || guid == Guid.Empty)
            return null;

        return new TemplateId(guid);
    }

    private async Task<IReadOnlyList<Template>> GetLiveTemplatesForCurrentTenantAsync(
        CancellationToken cancellationToken)
    {
        var tenantTemplateIds = await tenantTemplateResolver.GetTemplateIdsForCurrentTenantAsync(cancellationToken);
        if (tenantTemplateIds.Count == 0)
            return Array.Empty<Template>();

        return await new GetLiveTemplatesByIdsQueryObject(tenantTemplateIds)
            .Apply(templateRepo.Query().AsNoTracking())
            .ToListAsync(cancellationToken);
    }
}
