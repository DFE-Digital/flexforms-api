using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Domain rules for self-registration / auto-registration form access.
/// Template grants are applied on the <see cref="Entities.User"/> aggregate via <c>IUserFactory</c>
/// as <c>Permissions</c> with <c>ResourceType.Template</c>.
/// </summary>
public static class SelfRegistrationAccessRules
{
    /// <summary>
    /// Templates to auto-grant on self-registration:
    /// none if the tenant has no live forms; the single live form if there is exactly one;
    /// otherwise nothing unless <paramref name="defaultTemplateId"/> is one of the live forms.
    /// Admins assign further templates in User Manager.
    /// </summary>
    public static IReadOnlyList<TemplateId> ResolveAutoGrantedTemplates(
        IReadOnlyList<TemplateId> liveTemplateIds,
        TemplateId? defaultTemplateId = null)
    {
        if (liveTemplateIds is null || liveTemplateIds.Count == 0)
            return Array.Empty<TemplateId>();

        var live = liveTemplateIds
            .Where(id => id is not null)
            .Distinct()
            .ToList();

        if (live.Count == 0)
            return Array.Empty<TemplateId>();

        if (live.Count == 1)
            return live;

        if (defaultTemplateId is not null && live.Contains(defaultTemplateId))
            return [defaultTemplateId];

        return Array.Empty<TemplateId>();
    }

    /// <summary>
    /// Returns true when the user already has any template permission for the given template.
    /// </summary>
    public static bool HasTemplateAccess(Entities.User user, TemplateId templateId) =>
        UserTemplateAccess.HasAccess(user, templateId);
}
