using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

public sealed class Application : BaseAggregateRoot, IEntity<ApplicationId>
{
    private readonly List<ApplicationResponse> _responses = new();
    private readonly List<File> _files = new();

    public ApplicationId? Id { get; private set; }
    public string ApplicationReference { get; private set; }
    public TemplateVersionId TemplateVersionId { get; private set; }
    public TemplateVersion? TemplateVersion { get; private set; }
    public DateTime CreatedOn { get; private set; }
    public UserId CreatedBy { get; private set; }
    public User? CreatedByUser { get; private set; }
    public ApplicationStatus? Status { get; private set; }
    public DateTime? LastModifiedOn { get; private set; }
    public DateTime? DeletedOn { get; private set; } = null;
    public UserId? DeletedBy { get; private set; }
    public User? DeletedByUser { get; private set; }
    public UserId? LastModifiedBy { get; private set; }
    public User? LastModifiedByUser { get; private set; }
    public IReadOnlyCollection<ApplicationResponse> Responses => _responses.AsReadOnly();
    public IReadOnlyCollection<File> Files => _files.AsReadOnly();

    private Application() { /* For EF Core */ }

    /// <summary>
    /// Constructs a new Application.
    /// Pass null for optional fields (Status, LastModifiedOn, LastModifiedBy).
    /// </summary>
    public Application(
        ApplicationId id,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        ApplicationStatus? status = null,
        DateTime? lastModifiedOn = null,
        UserId? lastModifiedBy = null,
        DateTime? deletedOn = null,
        UserId? deletedBy = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ApplicationReference = applicationReference?.Trim()
                               ?? throw new ArgumentNullException(nameof(applicationReference));
        TemplateVersionId = templateVersionId;
        CreatedOn = createdOn;
        CreatedBy = createdBy;
        Status = status;
        LastModifiedOn = lastModifiedOn;
        LastModifiedBy = lastModifiedBy;
        DeletedOn = deletedOn;
        DeletedBy = deletedBy;

        if (status is null)
        {
            Status = ApplicationStatus.Created;
        }
    }

    public void AddResponse(ApplicationResponse response)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));

        if (response.ApplicationId != Id)
            throw new InvalidOperationException("Response's ApplicationId must match the Application's Id");

        _responses.Add(response);
    }

    /// <summary>
    /// Updates the LastModified tracking for this application.
    /// </summary>
    public void UpdateLastModified(DateTime lastModifiedOn, UserId lastModifiedBy)
    {
        if (lastModifiedBy == null)
            throw new ArgumentNullException(nameof(lastModifiedBy));

        LastModifiedOn = lastModifiedOn;
        LastModifiedBy = lastModifiedBy;
    }

    /// <summary>
    /// Gets the most recent response for this application.
    /// </summary>
    public ApplicationResponse? GetLatestResponse()
    {
        return _responses.OrderByDescending(r => r.CreatedOn).FirstOrDefault();
    }

    /// <summary>
    /// Submits the application, setting its status to Submitted and updating last modified tracking.
    /// </summary>
    public void Submit(DateTime submittedOn, UserId submittedBy, string userEmail, string userFullName)
    {
        if (submittedBy == null)
            throw new ArgumentNullException(nameof(submittedBy));
        
        if (string.IsNullOrWhiteSpace(userEmail))
            throw new ArgumentException("User email cannot be null or empty", nameof(userEmail));
            
        if (string.IsNullOrWhiteSpace(userFullName))
            throw new ArgumentException("User full name cannot be null or empty", nameof(userFullName));

        if (Status == ApplicationStatus.Submitted)
            throw new InvalidOperationException("Application has already been submitted");

        Status = ApplicationStatus.Submitted;
        LastModifiedOn = submittedOn;
        LastModifiedBy = submittedBy;
        
        // Raise domain event
        AddDomainEvent(new ApplicationSubmittedEvent(
            Id!,
            ApplicationReference,
            TemplateVersion!.TemplateId,
            submittedBy,
            userEmail,
            userFullName,
            submittedOn));
    }

    /// <summary>
    /// Deletes the application, setting its status to Deleted and updating last modified tracking.
    /// </summary>
    public void Delete(DateTime deletedOn, UserId deletedBy, string userEmail, string userFullName)
    {
        if (deletedBy == null)
            throw new ArgumentNullException(nameof(deletedBy));

        if (string.IsNullOrWhiteSpace(userEmail))
            throw new ArgumentException("User email cannot be null or empty", nameof(userEmail));

        if (string.IsNullOrWhiteSpace(userFullName))
            throw new ArgumentException("User full name cannot be null or empty", nameof(userFullName));

        Status = ApplicationStatus.Deleted;
        DeletedOn = deletedOn;
        DeletedBy = deletedBy;
        LastModifiedOn = deletedOn;
        LastModifiedBy = deletedBy;

        // Raise domain event
        AddDomainEvent(new ApplicationDeletedEvent(
            Id!,
            ApplicationReference,
            TemplateVersion!.TemplateId,
            deletedBy,
            userEmail,
            userFullName,
            deletedOn));
    }
}
