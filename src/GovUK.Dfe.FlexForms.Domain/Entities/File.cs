using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Entities;

public sealed class File : BaseAggregateRoot, IEntity<FileId>
{
    public const int ValidationMessageMaxLength = 1000;
    public const int ValidationSourceMaxLength = 256;

    public FileId? Id { get; private set; }
    public ApplicationId ApplicationId { get; private set; }
    public Application? Application { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public long FileSize { get; private set; }
    public string Path { get; private set; }
    public DateTime UploadedOn { get; private set; }
    public UserId UploadedBy { get; private set; }
    public User? UploadedByUser { get; private set; }
    public bool IsDeleted { get; private set; }
    public FileValidationStatus ValidationStatus { get; private set; } = FileValidationStatus.NotRequired;
    public string? ValidationMessage { get; private set; }
    public DateTime? ValidatedOn { get; private set; }
    public string? ValidationSource { get; private set; }

    private File() { /* For EF Core */ }

    public File(
        FileId id,
        ApplicationId applicationId,
        string name,
        string? description,
        string originalFileName,
        string fileName,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy,
        long fileSize)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        OriginalFileName = originalFileName ?? throw new ArgumentNullException(nameof(originalFileName));
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        Path = path;
        UploadedOn = uploadedOn;
        UploadedBy = uploadedBy ?? throw new ArgumentNullException(nameof(uploadedBy));
        FileSize = fileSize;
        ValidationStatus = FileValidationStatus.NotRequired;
    }

    public void Delete()
    {
        if (IsDeleted)
            throw new InvalidOperationException("File is already deleted.");
        IsDeleted = true;
    }

    public void SetApplication(Application application)
    {
        if (application == null)
            throw new ArgumentNullException(nameof(application));

        if (application.Id != ApplicationId)
            throw new InvalidOperationException("Application Id must match the File's ApplicationId");

        Application = application;
    }

    /// <summary>
    /// Marks the file as awaiting an external tenant validation result.
    /// </summary>
    public void RequireExternalValidation()
    {
        ValidationStatus = FileValidationStatus.Pending;
        ValidationMessage = null;
        ValidatedOn = null;
        ValidationSource = null;
    }

    /// <summary>
    /// Records a tenant function's validation outcome. Allowed while Pending, Passed, or Failed
    /// so validators can re-run. NotRequired files cannot be reported against.
    /// </summary>
    public void RecordValidationResult(bool isValid, string? message, DateTime reportedAt, string? source)
    {
        if (ValidationStatus == FileValidationStatus.NotRequired)
            throw new InvalidOperationException("File does not require external validation.");

        ValidationStatus = isValid ? FileValidationStatus.Passed : FileValidationStatus.Failed;
        ValidationMessage = Truncate(message, ValidationMessageMaxLength);
        ValidatedOn = reportedAt;
        ValidationSource = Truncate(source, ValidationSourceMaxLength);

        AddDomainEvent(new FileValidationRecordedEvent(
            Id!,
            ApplicationId,
            ValidationStatus,
            ValidationMessage,
            OriginalFileName,
            UploadedBy,
            reportedAt));
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
} 
