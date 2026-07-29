using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <inheritdoc />
public sealed class SelfRegistrationTemplateAccessService(
    IEaRepository<Template> templateRepo,
    ITenantTemplateResolver tenantTemplateResolver,
    IUserFactory userFactory) : ISelfRegistrationTemplateAccessService
{
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

        var toGrant = SelfRegistrationAccessRules.ResolveAutoGrantedTemplates(liveIds);
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
