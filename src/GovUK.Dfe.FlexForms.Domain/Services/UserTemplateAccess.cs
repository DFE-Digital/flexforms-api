using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Helpers for reading template (form) access from the unified <see cref="Permission"/> store
/// (<see cref="ResourceType.Template"/> grants).
/// </summary>
public static class UserTemplateAccess
{
    public static IEnumerable<Permission> GetTemplateGrants(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Permissions.Where(p => p.ResourceType == ResourceType.Template);
    }

    public static IReadOnlyList<TemplateId> GetTemplateIds(User user)
    {
        return GetTemplateGrants(user)
            .Select(TryParseTemplateId)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToList();
    }

    public static bool HasAccess(User user, TemplateId templateId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(templateId);

        var key = templateId.Value.ToString();
        return user.Permissions.Any(p =>
            p.ResourceType == ResourceType.Template
            && string.Equals(p.ResourceKey, key, StringComparison.OrdinalIgnoreCase));
    }

    private static TemplateId? TryParseTemplateId(Permission permission)
    {
        if (!Guid.TryParse(permission.ResourceKey, out var id) || id == Guid.Empty)
            return null;

        return new TemplateId(id);
    }
}
