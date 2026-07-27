using GovUK.Dfe.FlexForms.Domain.Common;

namespace GovUK.Dfe.FlexForms.Domain.ValueObjects
{
    public record RoleId(Guid Value) : IStronglyTypedId;
    public record UserId(Guid Value) : IStronglyTypedId;
    public record TemplateId(Guid Value) : IStronglyTypedId;
    public record TemplateVersionId(Guid Value) : IStronglyTypedId;
    public record ApplicationId(Guid Value) : IStronglyTypedId;
    public record ResponseId(Guid Value) : IStronglyTypedId;
    public record PermissionId(Guid Value) : IStronglyTypedId;
    public record TaskAssignmentLabelId(Guid Value) : IStronglyTypedId;
    public record TemplatePermissionId(Guid Value) : IStronglyTypedId;
    public record FileId(Guid Value) : IStronglyTypedId;
    public record CustomApplicationStatusId(Guid Value) : IStronglyTypedId;
    public record TenantMembershipId(Guid Value) : IStronglyTypedId;
    public record RolePermissionId(Guid Value) : IStronglyTypedId;
}
