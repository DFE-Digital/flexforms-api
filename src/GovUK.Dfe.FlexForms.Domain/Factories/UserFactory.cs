using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using System.Security;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Factories;

public class UserFactory : IUserFactory
{
    public User CreateContributor(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        DateTime? createdOn = null)
    {
        if (id == null)
            throw new ArgumentException("Id cannot be null", nameof(id));
        
        if (roleId == null)
            throw new ArgumentException("RoleId cannot be null", nameof(roleId));
        
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));
        
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
        
        if (createdBy == null)
            throw new ArgumentException("CreatedBy cannot be null", nameof(createdBy));
        
        if (applicationId == null)
            throw new ArgumentException("ApplicationId cannot be null", nameof(applicationId));

        if (string.IsNullOrWhiteSpace(applicationReference))
            throw new ArgumentException("ApplicationReference cannot be null or empty", nameof(applicationReference));

        if (templateId == null)
            throw new ArgumentException("TemplateId cannot be null", nameof(templateId));

        var when = createdOn ?? DateTime.UtcNow;
        
        var contributor = new User(
            id,
            roleId,
            name,
            email,
            when,
            createdBy,
            null,
            null);

        // Required so GetMyPermissions and other self-service endpoints can load claims for this user
        AddPermissionToUser(
            contributor,
            email,
            ResourceType.User,
            new[] { AccessType.Read },
            createdBy,
            null,
            when);

        // Add all required permissions directly (idempotent)
        
        // Application permissions
        AddPermissionToUser(
            contributor,
            applicationId.Value.ToString(),
            ResourceType.Application,
            new[] { AccessType.Read, AccessType.Write },
            createdBy,
            applicationId,
            when);

        // Application files permissions
        AddPermissionToUser(
            contributor,
            applicationId.Value.ToString(),
            ResourceType.ApplicationFiles,
            new[] { AccessType.Read, AccessType.Write, AccessType.Delete },
            createdBy,
            applicationId,
            when);

        // Notifications permissions
        AddPermissionToUser(
            contributor,
            email,
            ResourceType.Notifications,
            new[] { AccessType.Read, AccessType.Write, AccessType.Delete },
            createdBy,
            applicationId,
            when);

        // Template permissions
        AddTemplatePermissionToUser(
            contributor,
            templateId.Value.ToString(),
            new[] { AccessType.Read, AccessType.Write },
            createdBy,
            when);

        // Raise domain event for contributor addition (side effects like email)
        contributor.AddDomainEvent(new ContributorAddedEvent(
            applicationId,
            applicationReference,
            templateId,
            contributor,
            createdBy,
            when));

        return contributor;
    }

    public User CreateUser(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        TemplateId? templateId = null,
        DateTime? createdOn = null)
    {
        if (id == null)
            throw new ArgumentException("Id cannot be null", nameof(id));
        
        if (roleId == null)
            throw new ArgumentException("RoleId cannot be null", nameof(roleId));
        
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));
        
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));

        var when = createdOn ?? DateTime.UtcNow;
        
        var user = new User(
            id,
            roleId,
            name,
            email,
            when,
            null, // CreatedBy is null for self-registered users
            null,
            null);

        // Add self permission to the user to read their own user record
        AddPermissionToUser(
            user,
            email,
            ResourceType.User,
            new[] { AccessType.Read },
            id, // User grants permission to themselves
            null, // No application context
            when);

        // Add notification permissions for the user's own email
        AddPermissionToUser(
            user,
            email,
            ResourceType.Notifications,
            new[] { AccessType.Read, AccessType.Write, AccessType.Delete },
            id, // User grants permission to themselves
            null, // No application context
            when);

        // Add template permissions only when a template was resolved for this registration
        if (templateId is not null)
        {
            AddTemplatePermissionToUser(
                user,
                templateId.Value.ToString(),
                new[] { AccessType.Read, AccessType.Write },
                id, // User grants permission to themselves
                when);
        }

        // Raise domain event for user creation (side effects like email)
        user.AddDomainEvent(new UserCreatedEvent(
            user,
            when));

        return user;
    }

    /// <inheritdoc />
    public User CreateStandardUser(
        UserId id,
        string name,
        string email,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? createdOn = null)
    {
        if (id == null)
            throw new ArgumentException("Id cannot be null", nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));

        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        var templateIdList = templateIds?.ToList() ?? throw new ArgumentNullException(nameof(templateIds));
        if (templateIdList.Count == 0)
            throw new ArgumentException("At least one template ID is required for standard user provisioning", nameof(templateIds));

        var when = createdOn ?? DateTime.UtcNow;

        var user = new User(
            id,
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            when,
            grantedBy,
            null,
            null);

        GrantStandardUserPermissions(user, templateIdList, grantedBy, when);
        user.AddDomainEvent(new UserCreatedEvent(user, when));
        return user;
    }

    /// <inheritdoc />
    public void GrantStandardUserAccess(
        User user,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? grantedOn = null)
    {
        if (user == null)
            throw new ArgumentException("User cannot be null", nameof(user));

        var templateIdList = templateIds?.ToList() ?? throw new ArgumentNullException(nameof(templateIds));
        if (templateIdList.Count == 0)
            throw new ArgumentException("At least one template ID is required for standard user provisioning", nameof(templateIds));

        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        var when = grantedOn ?? DateTime.UtcNow;

        user.AssignRole(new RoleId(RoleConstants.UserRoleId));
        GrantStandardUserPermissions(user, templateIdList, grantedBy, when);
    }

    /// <inheritdoc />
    public User CreateAdmin(
        UserId id,
        string name,
        string email,
        UserId grantedBy,
        DateTime? createdOn = null)
    {
        if (id == null)
            throw new ArgumentException("Id cannot be null", nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));

        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        var when = createdOn ?? DateTime.UtcNow;

        var user = new User(
            id,
            new RoleId(RoleConstants.AdminRoleId),
            name,
            email,
            when,
            grantedBy,
            null,
            null);

        GrantAdminPermissions(user, grantedBy, when);
        return user;
    }

    /// <inheritdoc />
    public void GrantAdminAccess(
        User user,
        UserId grantedBy,
        DateTime? grantedOn = null)
    {
        if (user == null)
            throw new ArgumentException("User cannot be null", nameof(user));

        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        var when = grantedOn ?? DateTime.UtcNow;

        user.AssignRole(new RoleId(RoleConstants.AdminRoleId));
        GrantAdminPermissions(user, grantedBy, when);
    }

    /// <inheritdoc />
    public User CreateCaseworker(
        UserId id,
        string name,
        string email,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? createdOn = null)
    {
        if (id == null)
            throw new ArgumentException("Id cannot be null", nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));

        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        var templateIdList = templateIds?.ToList() ?? throw new ArgumentNullException(nameof(templateIds));
        if (templateIdList.Count == 0)
            throw new ArgumentException("At least one template ID is required for caseworker provisioning", nameof(templateIds));

        var when = createdOn ?? DateTime.UtcNow;

        var user = CreateUser(id, new RoleId(RoleConstants.CaseworkerRoleId), name, email, templateIds.FirstOrDefault(), when);

        GrantCaseworkerPermissions(user, templateIdList, grantedBy, when);
        return user;
    }

    /// <inheritdoc />
    public void GrantCaseworkerAccess(
        User user,
        IEnumerable<TemplateId> templateIds,
        UserId grantedBy,
        DateTime? grantedOn = null)
    {
        if (user == null)
            throw new ArgumentException("User cannot be null", nameof(user));

        var templateIdList = templateIds?.ToList() ?? throw new ArgumentNullException(nameof(templateIds));
        if (templateIdList.Count == 0)
            throw new ArgumentException("At least one template ID is required for caseworker provisioning", nameof(templateIds));

        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        var when = grantedOn ?? DateTime.UtcNow;

        user.AssignRole(new RoleId(RoleConstants.CaseworkerRoleId));

        GrantCaseworkerPermissions(user, templateIdList, grantedBy, when);
    }

    private void GrantCaseworkerPermissions(
        User user,
        IReadOnlyCollection<TemplateId> templateIds,
        UserId grantedBy,
        DateTime when)
    {
        AddPermissionToUser(
            user,
            user.Email,
            ResourceType.User,
            new[] { AccessType.Read },
            grantedBy,
            null,
            when);

        AddPermissionToUser(
            user,
            PermissionConstants.AnyResourceKey,
            ResourceType.Application,
            new[] { AccessType.Read },
            grantedBy,
            null,
            when);

        AddPermissionToUser(
            user,
            PermissionConstants.AnyResourceKey,
            ResourceType.ApplicationFiles,
            new[] { AccessType.Read },
            grantedBy,
            null,
            when);

        foreach (var templateId in templateIds)
        {
            AddTemplatePermissionToUser(
                user,
                templateId.Value.ToString(),
                new[] { AccessType.Read },
                grantedBy,
                when);
        }
    }

    private void GrantStandardUserPermissions(
        User user,
        IReadOnlyCollection<TemplateId> templateIds,
        UserId grantedBy,
        DateTime when)
    {
        AddPermissionToUser(
            user,
            user.Email,
            ResourceType.User,
            new[] { AccessType.Read },
            grantedBy,
            null,
            when);

        AddPermissionToUser(
            user,
            user.Email,
            ResourceType.Notifications,
            new[] { AccessType.Read, AccessType.Write, AccessType.Delete },
            grantedBy,
            null,
            when);

        foreach (var templateId in templateIds)
        {
            AddTemplatePermissionToUser(
                user,
                templateId.Value.ToString(),
                new[] { AccessType.Read, AccessType.Write },
                grantedBy,
                when);
        }
    }

    private void GrantAdminPermissions(User user, UserId grantedBy, DateTime when)
    {
        AddPermissionToUser(
            user,
            user.Email,
            ResourceType.User,
            new[] { AccessType.Read },
            grantedBy,
            null,
            when);
    }

    public void AddPermissionToUser(
        User user,
        string resourceKey,
        ResourceType resourceType,
        AccessType[] accessTypes,
        UserId grantedBy,
        ApplicationId? applicationId = null,
        DateTime? grantedOn = null)
    {
        if (user == null)
            throw new ArgumentException("User cannot be null", nameof(user));
        
        if (string.IsNullOrWhiteSpace(resourceKey))
            throw new ArgumentException("ResourceKey cannot be null or empty", nameof(resourceKey));
        
        if (accessTypes == null)
            throw new ArgumentException("AccessTypes cannot be null", nameof(accessTypes));
        
        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        var when = grantedOn ?? DateTime.UtcNow;

        foreach (var accessType in accessTypes)
        {
            // Check if permission already exists (idempotent)
            var hasPermission = user.Permissions
                .Any(p => p.ResourceType == resourceType && 
                         p.ResourceKey == resourceKey && 
                         p.AccessType == accessType &&
                         (applicationId == null || p.ApplicationId == applicationId));

            if (!hasPermission)
            {
                user.AddPermission(
                    resourceKey,
                    resourceType,
                    accessType,
                    grantedBy,
                    applicationId,
                    when);
            }
        }
    }

    public void AddTemplatePermissionToUser(
        User user,
        string templateId,
        AccessType[] accessTypes,
        UserId grantedBy,
        DateTime? grantedOn = null)
    {
        if (user == null)
            throw new ArgumentException("User cannot be null", nameof(user));
        
        if (string.IsNullOrWhiteSpace(templateId))
            throw new ArgumentException("TemplateId cannot be null or empty", nameof(templateId));
        
        if (accessTypes == null)
            throw new ArgumentException("AccessTypes cannot be null", nameof(accessTypes));
        
        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        var when = grantedOn ?? DateTime.UtcNow;

        foreach (var accessType in accessTypes)
        {
            // Check if template permission already exists (idempotent)
            var hasTemplatePermission = user.TemplatePermissions
                .Any(tp => tp.TemplateId.Value.ToString() == templateId && tp.AccessType == accessType);

            if (!hasTemplatePermission)
            {
                user.AddTemplatePermission(
                    templateId,
                    accessType,
                    grantedBy,
                    when);
            }
        }
    }

    /// <inheritdoc />
    public void EnsureUserHasTemplatePermission(
        User user,
        TemplateId templateId,
        UserId grantedBy,
        DateTime? grantedOn = null)
    {
        if (user == null)
            throw new ArgumentException("User cannot be null", nameof(user));
        if (templateId == null)
            throw new ArgumentException("TemplateId cannot be null", nameof(templateId));
        if (grantedBy == null)
            throw new ArgumentException("GrantedBy cannot be null", nameof(grantedBy));

        AddTemplatePermissionToUser(
            user,
            templateId.Value.ToString(),
            new[] { AccessType.Read, AccessType.Write },
            grantedBy,
            grantedOn);
    }

    public bool RemovePermissionFromUser(
        User user,
        Permission permission)
    {
        if (user == null)
            throw new ArgumentException("User cannot be null", nameof(user));
        
        if (permission == null)
            throw new ArgumentException("Permission cannot be null", nameof(permission));

        return user.RemovePermission(permission);
    }

    /// <inheritdoc />
    public int RemoveTemplatePermissionsFromUser(
        User user,
        IEnumerable<TemplateId> templateIds)
    {
        if (user == null)
            throw new ArgumentException("User cannot be null", nameof(user));
        if (templateIds == null)
            throw new ArgumentNullException(nameof(templateIds));

        var ids = templateIds.Select(t => t.Value).ToHashSet();
        if (ids.Count == 0)
            return 0;

        var toRemove = user.TemplatePermissions
            .Where(tp => ids.Contains(tp.TemplateId.Value))
            .ToList();

        var removed = 0;
        foreach (var permission in toRemove)
        {
            if (user.RemoveTemplatePermission(permission))
                removed++;
        }

        return removed;
    }
}
