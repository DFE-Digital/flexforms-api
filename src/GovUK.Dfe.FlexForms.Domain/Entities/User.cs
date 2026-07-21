using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

public sealed class User : BaseAggregateRoot, IEntity<UserId>
{
    public UserId? Id { get; private set; }
    public RoleId RoleId { get; private set; }
    public Role? Role { get; private set; }
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public DateTime CreatedOn { get; private set; }
    public UserId? CreatedBy { get; private set; }
    public User? CreatedByUser { get; private set; }
    public DateTime? LastModifiedOn { get; private set; }
    public UserId? LastModifiedBy { get; private set; }
    public User? LastModifiedByUser { get; private set; }
    public string? ExternalProviderId { get; private set; }

    private readonly List<Permission> _permissions = new();
    private readonly List<TemplatePermission> _templatePermissions = new();
    private readonly List<File> _files = new();

    public IReadOnlyCollection<Permission> Permissions
        => _permissions.AsReadOnly();

    public IReadOnlyCollection<TemplatePermission> TemplatePermissions
        => _templatePermissions.AsReadOnly();

    public IReadOnlyCollection<File> Files => _files.AsReadOnly();

    /// <summary>
    /// Assigns a new role to the user.
    /// </summary>
    public void AssignRole(RoleId roleId)
    {
        RoleId = roleId ?? throw new ArgumentNullException(nameof(roleId));
    }

    private User()
    {
        // Required by EF Core to materialise the entity.
    }

    /// <summary>
    /// Constructs a new User with all required fields. 
    /// Pass in null for optional fields (CreatedBy, LastModifiedOn, LastModifiedBy).
    /// </summary>
    public User(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        DateTime createdOn,
        UserId? createdBy,
        DateTime? lastModifiedOn,
        UserId? lastModifiedBy,
        string? externalProviderId = null,
        IEnumerable<Permission>? initialPermissions = null,
        IEnumerable<TemplatePermission>? initialTemplatePermissions = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        RoleId = roleId ?? throw new ArgumentNullException(nameof(roleId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Email = (email ?? throw new ArgumentNullException(nameof(email))).Trim();
        CreatedOn = createdOn;
        CreatedBy = createdBy;
        LastModifiedOn = lastModifiedOn;
        LastModifiedBy = lastModifiedBy;
        ExternalProviderId = externalProviderId;

        if (initialPermissions != null)
        {
            _permissions.AddRange(initialPermissions);
        }

        if (initialTemplatePermissions != null)
        {
            _templatePermissions.AddRange(initialTemplatePermissions);
        }
    }

    /// <summary>
    /// Internal method to create and attach a new Permission to this User.
    /// This should only be called by the UserFactory.
    /// </summary>
    internal Permission AddPermission(
        string resourceKey,
        ResourceType resourceType,
        AccessType accessType,
        UserId grantedBy,
        ApplicationId? applicationId = null,
        DateTime? grantedOn = null)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
            throw new ArgumentException("ResourceKey cannot be empty", nameof(resourceKey));

        var id = new PermissionId(Guid.NewGuid());
        var when = grantedOn ?? DateTime.UtcNow;

        var permission = new Permission(
            id,
            this.Id ?? throw new InvalidOperationException("UserId must be set before adding a permission."),
            applicationId,
            resourceKey,
            resourceType,
            accessType,
            when,
            grantedBy);

        _permissions.Add(permission);
        return permission;
    }

    /// <summary>
    /// Internal method to remove a Permission from this User.
    /// This should only be called by the UserFactory.
    /// </summary>
    internal bool RemovePermission(Permission permission)
    {
        if (permission == null)
            throw new ArgumentNullException(nameof(permission));

        return _permissions.Remove(permission);
    }

    /// <summary>
    /// Internal method to create and attach a new TemplatePermission to this User.
    /// This should only be called by the UserFactory.
    /// </summary>
    internal TemplatePermission AddTemplatePermission(
        string templateId,
        AccessType accessType,
        UserId grantedBy,
        DateTime? grantedOn = null)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            throw new ArgumentException("TemplateId cannot be empty", nameof(templateId));

        var id = new TemplatePermissionId(Guid.NewGuid());
        var when = grantedOn ?? DateTime.UtcNow;

        var templatePermission = new TemplatePermission(
            id,
            this.Id ?? throw new InvalidOperationException("UserId must be set before adding a template permission."),
            new TemplateId(new Guid(templateId)),
            accessType,
            when,
            grantedBy);

        _templatePermissions.Add(templatePermission);
        return templatePermission;
    }

    /// <summary>
    /// Internal method to remove a TemplatePermission from this User.
    /// This should only be called by the UserFactory.
    /// </summary>
    internal bool RemoveTemplatePermission(TemplatePermission templatePermission)
    {
        if (templatePermission == null)
            throw new ArgumentNullException(nameof(templatePermission));

        return _templatePermissions.Remove(templatePermission);
    }
}
