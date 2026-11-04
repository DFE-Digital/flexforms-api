using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

public sealed class Template : BaseAggregateRoot, IEntity<TemplateId>
{
    private readonly List<TemplateVersion> _templateVersions = new();

    public TemplateId? Id { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTime CreatedOn { get; private set; }
    public UserId CreatedBy { get; private set; }
    public User? CreatedByUser { get; private set; }

    /// <summary>
    /// Identifies the tenant that owns templates created through tenant administration.
    /// Legacy templates may be null and remain associated through configured mappings.
    /// </summary>
    public Guid? TenantId { get; private set; }

    /// <summary>
    /// When <c>true</c>, end users with permission can access this template.
    /// Admins and template managers can always access templates regardless of this flag.
    /// </summary>
    public bool IsLive { get; private set; }

    public IReadOnlyCollection<TemplateVersion> TemplateVersions => _templateVersions.AsReadOnly();

    private Template() { /* For EF Core */ }

    /// <summary>
    /// Constructs a new Template. New templates start as not live (draft) until an admin publishes them.
    /// </summary>
    public Template(
        TemplateId id,
        string name,
        DateTime createdOn,
        UserId createdBy,
        bool isLive = false,
        Guid? tenantId = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        CreatedOn = createdOn;
        CreatedBy = createdBy;
        IsLive = isLive;
        TenantId = tenantId;
    }

    /// <summary>
    /// Sets whether this template is live for end users in the tenant.
    /// </summary>
    public void SetLive(bool isLive) => IsLive = isLive;

    /// <summary>
    /// Adds a new version to this template.
    /// </summary>
    public void AddVersion(TemplateVersion version)
    {
        if (version == null)
            throw new ArgumentNullException(nameof(version));

        if (version.TemplateId != Id)
            throw new InvalidOperationException("Template version must belong to this template");

        _templateVersions.Add(version);
    }
}
