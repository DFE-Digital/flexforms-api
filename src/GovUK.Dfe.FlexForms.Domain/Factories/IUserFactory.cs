using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Factories;

public interface IUserFactory
{
    User CreateContributor(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        DateTime? createdOn = null);

    User CreateUser(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        TemplateId? templateId = null,
        DateTime? createdOn = null);

    /// <summary>
    /// Creates a new standard user with the User role and access to the given templates.
    /// </summary>
    User CreateStandardUser(
        UserId id,
        string name,
        string email,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? createdOn = null);

    /// <summary>
    /// Assigns the User role and standard permissions to an existing user.
    /// </summary>
    void GrantStandardUserAccess(
        User user,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? grantedOn = null);

    /// <summary>
    /// Creates a new admin user with the Admin role.
    /// </summary>
    User CreateAdmin(
        UserId id,
        string name,
        string email,
        UserId grantedBy,
        DateTime? createdOn = null);

    /// <summary>
    /// Assigns the Admin role to an existing user.
    /// </summary>
    void GrantAdminAccess(
        User user,
        UserId grantedBy,
        DateTime? grantedOn = null);

    /// <summary>
    /// Creates a new caseworker with read-only tenant-wide application access scoped to the given templates.
    /// </summary>
    User CreateCaseworker(
        UserId id,
        string name,
        string email,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? createdOn = null);

    /// <summary>
    /// Grants caseworker read permissions and template access to an existing user, and assigns the Caseworker role.
    /// </summary>
    void GrantCaseworkerAccess(
        User user,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? grantedOn = null);

    void AddPermissionToUser(
        User user,
        string resourceKey,
        ResourceType resourceType,
        AccessType[] accessTypes,
        UserId grantedBy,
        ApplicationId? applicationId = null,
        DateTime? grantedOn = null);

    void AddTemplatePermissionToUser(
        User user,
        string templateId,
        AccessType[] accessTypes,
        UserId grantedBy,
        DateTime? grantedOn = null);

    /// <summary>
    /// Ensures the user has Read and Write template permission for the given template (idempotent).
    /// Call from registration or other flows when a user must have access to a template.
    /// </summary>
    void EnsureUserHasTemplatePermission(
        User user,
        TemplateId templateId,
        UserId grantedBy,
        DateTime? grantedOn = null);

    bool RemovePermissionFromUser(
        User user,
        Permission permission);
} 
