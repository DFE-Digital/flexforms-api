using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Resolves which applications a user can list based on their role and permission grants.
/// </summary>
public static class ApplicationAccessResolver
{
    /// <summary>
    /// Describes how application listing should be scoped for a user.
    /// </summary>
    public enum AccessMode
    {
        /// <summary>Only applications with explicit permission rows.</summary>
        SpecificApplicationIds,

        /// <summary>All applications in the tenant database.</summary>
        AllApplicationsInTenant,

        /// <summary>Applications belonging to templates the user can read.</summary>
        TemplateScoped
    }

    /// <summary>
    /// The resolved listing scope for a user.
    /// </summary>
    public sealed record AccessScope(
        AccessMode Mode,
        IReadOnlyCollection<ApplicationId> ApplicationIds,
        IReadOnlyCollection<TemplateId> TemplateIds)
    {
        public static AccessScope Empty { get; } = new(
            AccessMode.SpecificApplicationIds,
            Array.Empty<ApplicationId>(),
            Array.Empty<TemplateId>());
    }

    /// <summary>
    /// Resolves the application listing scope for the given user based on role and grants.
    /// SuperAdmin sees all applications. Users with Application:Any:Read see all applications.
    /// Otherwise only applications with explicit permission rows.
    /// </summary>
    public static AccessScope Resolve(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var roleName = user.Role?.Name;

        if (string.Equals(roleName, RoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, RoleNames.Admin, StringComparison.OrdinalIgnoreCase))
            return new AccessScope(AccessMode.AllApplicationsInTenant, Array.Empty<ApplicationId>(), Array.Empty<TemplateId>());

        if (HasTenantWideApplicationRead(user))
            return new AccessScope(AccessMode.AllApplicationsInTenant, Array.Empty<ApplicationId>(), Array.Empty<TemplateId>());

        var applicationIds = user.Permissions
            .Where(p => p is { ApplicationId: not null, ResourceType: ResourceType.Application })
            .Select(p => p.ApplicationId!)
            .Distinct()
            .ToList();

        return new AccessScope(AccessMode.SpecificApplicationIds, applicationIds, Array.Empty<TemplateId>());
    }

    /// <summary>
    /// Returns true when the user may list all applications for the specified template
    /// (admin, or tenant-wide application read access).
    /// </summary>
    public static bool CanListAllApplicationsForTemplate(User user, TemplateId templateId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(templateId);

        var scope = Resolve(user);
        return scope.Mode switch
        {
            AccessMode.AllApplicationsInTenant => true,
            AccessMode.TemplateScoped => scope.TemplateIds.Any(id => id.Value == templateId.Value),
            _ => false
        };
    }

    private static bool HasTenantWideApplicationRead(User user) =>
        user.Permissions.Any(p =>
            p.ResourceType == ResourceType.Application
            && string.Equals(p.ResourceKey, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase)
            && p.AccessType == AccessType.Read);
}
