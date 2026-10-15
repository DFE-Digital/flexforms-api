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
    public const string DefaultTemplateIdsKey = "SelfRegistration:DefaultTemplateIds";
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
            ReadDefaultTemplateIds());
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

    /// <summary>
    /// Union of <c>SelfRegistration:DefaultTemplateIds</c> (JSON array, or a comma/semicolon-separated string)
    /// and <c>SelfRegistration:DefaultTemplateId</c>. The legacy <c>ExternalApplicationsApiClient:DefaultTemplateId</c>
    /// is only used when neither yields a valid id. Invalid or empty GUIDs are ignored.
    /// </summary>
    private IReadOnlyList<TemplateId> ReadDefaultTemplateIds()
    {
        var settings = tenantContextAccessor.CurrentTenant?.Settings;
        if (settings is null)
            return Array.Empty<TemplateId>();

        var rawValues = new List<string?>();

        var listSection = settings.GetSection(DefaultTemplateIdsKey);
        rawValues.AddRange(listSection.GetChildren().Select(child => child.Value));
        if (!string.IsNullOrWhiteSpace(listSection.Value))
        {
            rawValues.AddRange(listSection.Value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        rawValues.Add(settings[DefaultTemplateIdKey]);

        var ids = ParseTemplateIds(rawValues);
        return ids.Count > 0
            ? ids
            : ParseTemplateIds([settings[LegacyDefaultTemplateIdKey]]);
    }

    private static IReadOnlyList<TemplateId> ParseTemplateIds(IEnumerable<string?> rawValues) =>
        rawValues
            .Select(raw => Guid.TryParse(raw?.Trim(), out var guid) ? guid : Guid.Empty)
            .Where(guid => guid != Guid.Empty)
            .Distinct()
            .Select(guid => new TemplateId(guid))
            .ToList();

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
