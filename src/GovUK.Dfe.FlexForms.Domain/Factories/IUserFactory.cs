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
        DateTime? createdOn = null,
        Guid? tenantId = null);

    User CreateUser(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        TemplateId? templateId = null,
        DateTime? createdOn = null,
        Guid? tenantId = null);

    /// <summary>
    /// Creates a new standard user with the User role and access to the given templates.
    /// </summary>
    User CreateStandardUser(
        UserId id,
        string name,
        string email,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? createdOn = null,
        Guid? tenantId = null);

    /// <summary>
    /// Assigns the User role and standard permissions to an existing user.
    /// </summary>
    void GrantStandardUserAccess(
        User user,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? grantedOn = null,
        Guid? tenantId = null);

    /// <summary>
    /// Creates a new admin user with the tenant-scoped Admin role.
    /// </summary>
    /// <param name="tenantAdminRoleId">Per-tenant Admin role id (never the platform SuperAdmin id).</param>
    User CreateAdmin(
        UserId id,
        RoleId tenantAdminRoleId,
        string name,
        string email,
        UserId grantedBy,
        DateTime? createdOn = null);

    /// <summary>
    /// Assigns the tenant-scoped Admin role to an existing user.
    /// </summary>
    /// <param name="tenantAdminRoleId">Per-tenant Admin role id (never the platform SuperAdmin id).</param>
    void GrantAdminAccess(
        User user,
        RoleId tenantAdminRoleId,
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

    /// <summary>
    /// Removes template permissions for the given template IDs from the user (idempotent).
    /// </summary>
    int RemoveTemplatePermissionsFromUser(
        User user,
        IEnumerable<TemplateId> templateIds);
} 
