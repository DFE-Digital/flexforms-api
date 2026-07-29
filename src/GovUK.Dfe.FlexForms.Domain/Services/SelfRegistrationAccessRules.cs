using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Domain rules for self-registration / auto-registration form access.
/// Template grants are applied on the <see cref="Entities.User"/> aggregate via <c>IUserFactory</c>.
/// </summary>
public static class SelfRegistrationAccessRules
{
    /// <summary>
    /// Self-registration auto-grants Template R/W for every live template in the tenant catalogue.
    /// Zero live templates means no form access until an admin assigns templates.
    /// </summary>
    public static IReadOnlyList<TemplateId> ResolveAutoGrantedTemplates(IReadOnlyList<TemplateId> liveTemplateIds)
    {
        if (liveTemplateIds is null || liveTemplateIds.Count == 0)
            return Array.Empty<TemplateId>();

        return liveTemplateIds
            .Where(id => id is not null)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Returns true when the user already has any template permission for the given template.
    /// </summary>
    public static bool HasTemplateAccess(Entities.User user, TemplateId templateId)
    {
        if (user is null)
            throw new ArgumentNullException(nameof(user));
        if (templateId is null)
            throw new ArgumentNullException(nameof(templateId));

        return user.TemplatePermissions.Any(tp => tp.TemplateId.Value == templateId.Value);
    }
}
