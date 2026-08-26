namespace GovUK.Dfe.FlexForms.Application.Options;

/// <summary>
/// Well-known <c>sourceType: Metadata</c> keys that the platform populates when publishing
/// mapped events. Admins choose which to include by listing them in fieldMappings.
/// </summary>
public static class PlatformEventMetadataKeys
{
    // Always available (any trigger)
    public const string ApplicationId = "applicationId";
    public const string ApplicationReference = "applicationReference";

    // ApplicationSubmitted
    public const string SubmittedByUserId = "submittedByUserId";
    public const string SubmittedByEmail = "submittedByEmail";
    public const string SubmittedByFullName = "submittedByFullName";
    public const string SubmittedOn = "submittedOn";

    // FileUploaded
    public const string FileId = "fileId";
    public const string FileName = "fileName";
    public const string OriginalFileName = "originalFileName";
    public const string FilePath = "filePath";
    public const string FileUri = "fileUri";
    public const string FileHash = "fileHash";
    public const string FileSize = "fileSize";
    public const string UploaderUserId = "uploaderUserId";
    public const string UploaderEmail = "uploaderEmail";
    public const string UploadedOn = "uploadedOn";

    // Contributor emails (EmailPlaceholderMappings Metadata source)
    public const string ContributorName = "contributorName";
    public const string ContributorEmail = "contributorEmail";
    public const string AddedOn = "addedOn";
    public const string GrantedOn = "grantedOn";
    public const string AccessTypes = "accessTypes";

    /// <summary>Keys available on every trigger (application identity).</summary>
    public static IReadOnlyList<MetadataKeyHint> AlwaysAvailable { get; } =
    [
        new(ApplicationId, "Application id (GUID)"),
        new(ApplicationReference, "Human-readable application reference")
    ];

    /// <summary>Keys populated when the ApplicationSubmitted trigger fires.</summary>
    public static IReadOnlyList<MetadataKeyHint> ApplicationSubmitted { get; } =
    [
        ..AlwaysAvailable,
        new(SubmittedByUserId, "User id of the submitter"),
        new(SubmittedByEmail, "Email of the submitter"),
        new(SubmittedByFullName, "Full name of the submitter"),
        new(SubmittedOn, "UTC timestamp when the application was submitted")
    ];

    /// <summary>Keys populated when the FileUploaded trigger fires.</summary>
    public static IReadOnlyList<MetadataKeyHint> FileUploaded { get; } =
    [
        ..AlwaysAvailable,
        new(FileId, "Uploaded file id (GUID)"),
        new(FileName, "Stored / hashed file name on disk or share"),
        new(OriginalFileName, "Original file name as uploaded by the user"),
        new(FilePath, "Storage path (without SAS)"),
        new(FileUri, "Read URI including short-lived SAS (or local file:// in development)"),
        new(FileHash, "Content hash used for scanning"),
        new(FileSize, "File size in bytes"),
        new(UploaderUserId, "User id of the uploader"),
        new(UploaderEmail, "Email of the uploader when known"),
        new(UploadedOn, "UTC timestamp when the file was uploaded")
    ];

    /// <summary>Keys populated for contributor invitation emails.</summary>
    public static IReadOnlyList<MetadataKeyHint> ContributorInvited { get; } =
    [
        ..AlwaysAvailable,
        new(ContributorName, "Display name of the contributor"),
        new(ContributorEmail, "Email of the contributor"),
        new(AddedOn, "UTC timestamp when the contributor was added")
    ];

    /// <summary>Keys populated for contributor access-granted emails.</summary>
    public static IReadOnlyList<MetadataKeyHint> ContributorAccessGranted { get; } =
    [
        ..AlwaysAvailable,
        new(ContributorName, "Display name of the contributor"),
        new(ContributorEmail, "Email of the contributor"),
        new(GrantedOn, "UTC timestamp when access was granted"),
        new(AccessTypes, "Comma-separated access types granted")
    ];

    public sealed record MetadataKeyHint(string Key, string Description);
}
