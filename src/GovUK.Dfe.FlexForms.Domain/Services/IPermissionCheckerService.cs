using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Domain service for checking permissions based on claims.
/// This service operates on the domain level and is resource-type agnostic.
/// </summary>
public interface IPermissionCheckerService
{
    /// <summary>
    /// Checks if the current user has a specific permission for a resource
    /// </summary>
    /// <param name="resourceType">Type of the resource (e.g., Template, Application)</param>
    /// <param name="resourceId">Identifier of the resource</param>
    /// <param name="accessType">Type of access required</param>
    /// <returns>True if the user has the specified permission, false otherwise</returns>
    bool HasPermission(ResourceType resourceType, string resourceId, AccessType accessType);

    /// <summary>
    /// Checks if the current user has any permission of the specified type for any resource of the given type
    /// </summary>
    /// <param name="resourceType">Type of the resource (e.g., Template, Application)</param>
    /// <param name="accessType">Type of access required</param>
    /// <returns>True if the user has any matching permission, false otherwise</returns>
    bool HasAnyPermission(ResourceType resourceType, AccessType accessType);

    /// <summary>
    /// Gets all resource IDs of a specific type that the current user has permission to access
    /// </summary>
    /// <param name="resourceType">Type of the resource (e.g., Template, Application)</param>
    /// <param name="accessType">Type of access required</param>
    /// <returns>Collection of resource IDs the user has permission to access</returns>
    IReadOnlyCollection<string> GetResourceIdsWithPermission(ResourceType resourceType, AccessType accessType);

    /// <summary>
    /// Checks if the current user has a specific permission for a template
    /// </summary>
    /// <param name="templateId">Identifier of the template</param>
    /// <param name="accessType">Type of access required</param>
    /// <returns>True if the user has the specified permission, false otherwise</returns>
    bool HasTemplatePermission(string templateId, AccessType accessType);

    /// <summary>
    /// Checks if the current user is the owner of an application
    /// </summary>
    /// <param name="applicationId">Identifier of the application</param>
    /// <returns>True if the user is the owner of the application, false otherwise</returns>
    bool IsApplicationOwner(string applicationId);

    /// <summary>
    /// Checks if the current user is the owner of an application by comparing with the application's CreatedBy property
    /// </summary>
    /// <param name="application">The application entity</param>
    /// <param name="currentUserId">The current user's ID</param>
    /// <returns>True if the user is the owner of the application, false otherwise</returns>
    bool IsApplicationOwner(GovUK.Dfe.FlexForms.Domain.Entities.Application application, string currentUserId);

    /// <summary>
    /// Checks if the current user can manage contributors for an application
    /// </summary>
    /// <param name="applicationId">Identifier of the application</param>
    /// <returns>True if the user can manage contributors, false otherwise</returns>
    bool CanManageContributors(string applicationId);

    /// <summary>
    /// Checks if the current user has the Admin role.
    /// </summary>
    /// <returns>True if the user is an Admin, false otherwise</returns>
    bool IsAdmin();

    /// <summary>
    /// Checks if the current principal is an interactive tenant Admin (user JWT with Admin role).
    /// Returns false for machine identities (client credentials, API key, mTLS) even when they
    /// carry an Admin role claim.
    /// </summary>
    /// <returns>True if the caller is an interactive Admin user; otherwise false.</returns>
    bool IsInteractiveTenantAdmin();

    /// <summary>
    /// Checks if the current user can read all applications in the current tenant.
    /// </summary>
    /// <returns>True if the user can read all applications, false otherwise</returns>
    bool CanReadAllApplications();
}
