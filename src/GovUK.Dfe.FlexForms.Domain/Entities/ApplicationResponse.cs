using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

public sealed class ApplicationResponse : IEntity<ResponseId>
{
    public ResponseId? Id { get; private set; }
    public ApplicationId ApplicationId { get; private set; }
    public Application? Application { get; private set; }
    public string ResponseBody { get; private set; } = null!;
    public DateTime CreatedOn { get; private set; }
    public UserId CreatedBy { get; private set; }
    public User? CreatedByUser { get; private set; }
    public DateTime? LastModifiedOn { get; private set; }
    public UserId? LastModifiedBy { get; private set; }
    public User? LastModifiedByUser { get; private set; }

    /// <summary>
    /// The application's <see cref="Application.SourceRevision"/> when this response was created. Immutable once assigned.
    /// </summary>
    public long CreatedAtRevision { get; private set; }

    private ApplicationResponse() { /* For EF Core */ }

    /// <summary>
    /// Constructs a new ApplicationResponse. 
    /// </summary>
    public ApplicationResponse(
        ResponseId id,
        ApplicationId applicationId,
        string responseBody,
        DateTime createdOn,
        UserId createdBy,
        DateTime? lastModifiedOn = null,
        UserId? lastModifiedBy = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ApplicationId = applicationId;
        ResponseBody = responseBody ?? throw new ArgumentNullException(nameof(responseBody));
        CreatedOn = createdOn;
        CreatedBy = createdBy;
        LastModifiedOn = lastModifiedOn;
        LastModifiedBy = lastModifiedBy;
    }

    public void AssignCreatedAtRevision(long revision)
    {
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision), "Revision must be positive");

        if (CreatedAtRevision != 0 && CreatedAtRevision != revision)
            throw new InvalidOperationException("CreatedAtRevision is immutable once assigned");

        CreatedAtRevision = revision;
    }
}
