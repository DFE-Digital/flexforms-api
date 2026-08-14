using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Events;

public sealed record FileValidationRecordedEvent(
    FileId FileId,
    ApplicationId ApplicationId,
    FileValidationStatus Status,
    string? Message,
    string OriginalFileName,
    UserId UploadedBy,
    DateTime RecordedOn) : IDomainEvent
{
    public DateTime OccurredOn => RecordedOn;
}
