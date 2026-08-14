using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Events;

public sealed record ApplicationDeletedEvent(
    ApplicationId ApplicationId,
    string ApplicationReference,
    TemplateId TemplateId,
    UserId DeletedBy,
    string UserEmail,
    string UserFullName,
    DateTime DeletedOn) : IDomainEvent
{
    public DateTime OccurredOn => DeletedOn;
}
