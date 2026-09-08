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
    private readonly List<File> _files = new();

    public IReadOnlyCollection<Permission> Permissions
        => _permissions.AsReadOnly();

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
        IEnumerable<Permission>? initialPermissions = null)
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
}
