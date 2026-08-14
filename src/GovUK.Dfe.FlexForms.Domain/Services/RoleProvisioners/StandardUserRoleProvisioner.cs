using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services.RoleProvisioners;

/// <summary>
/// Provisions users with the standard User role for submitting and managing their own applications.
/// </summary>
public sealed class StandardUserRoleProvisioner(IUserFactory userFactory) : IUserRoleProvisioner
{
    /// <inheritdoc />
    public string RoleName => RoleNames.User;

    /// <inheritdoc />
    public bool RequiresTemplateIds => true;

    /// <inheritdoc />
    public User CreateUser(RoleAssignmentRequest request)
    {
        ValidateTemplateIds(request.TemplateIds);

        return userFactory.CreateStandardUser(
            new UserId(Guid.NewGuid()),
            request.Name,
            request.Email,
            request.TemplateIds,
            request.GrantedBy,
            request.GrantedOn,
            request.TenantId);
    }

    /// <inheritdoc />
    public void AssignToExistingUser(User user, RoleAssignmentRequest request)
    {
        ValidateTemplateIds(request.TemplateIds);

        userFactory.GrantStandardUserAccess(
            user,
            request.TemplateIds,
            request.GrantedBy,
            request.GrantedOn,
            request.TenantId);
    }

    private static void ValidateTemplateIds(IReadOnlyCollection<TemplateId> templateIds)
    {
        if (templateIds.Count == 0)
            throw new ArgumentException("At least one template ID is required for the User role.", nameof(templateIds));
    }
}
