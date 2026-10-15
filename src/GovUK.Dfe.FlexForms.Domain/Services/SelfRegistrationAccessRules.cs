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
    /// otherwise every entry in <paramref name="defaultTemplateIds"/> that is one of the live forms
    /// (nothing when none of the defaults are live).
    /// Admins assign further templates in User Manager.
    /// </summary>
    public static IReadOnlyList<TemplateId> ResolveAutoGrantedTemplates(
        IReadOnlyList<TemplateId> liveTemplateIds,
        IEnumerable<TemplateId>? defaultTemplateIds = null)
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

        if (defaultTemplateIds is null)
            return Array.Empty<TemplateId>();

        return defaultTemplateIds
            .Where(id => id is not null && live.Contains(id))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Single-default overload of <see cref="ResolveAutoGrantedTemplates(IReadOnlyList{TemplateId}, IEnumerable{TemplateId}?)"/>.
    /// </summary>
    public static IReadOnlyList<TemplateId> ResolveAutoGrantedTemplates(
        IReadOnlyList<TemplateId> liveTemplateIds,
        TemplateId? defaultTemplateId) =>
        ResolveAutoGrantedTemplates(
            liveTemplateIds,
            defaultTemplateId is null ? null : [defaultTemplateId]);

    /// <summary>
    /// Returns true when the user already has any template permission for the given template.
    /// </summary>
    public static bool HasTemplateAccess(Entities.User user, TemplateId templateId) =>
        UserTemplateAccess.HasAccess(user, templateId);
}
