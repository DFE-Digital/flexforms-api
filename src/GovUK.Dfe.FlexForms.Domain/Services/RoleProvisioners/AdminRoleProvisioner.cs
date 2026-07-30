using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services.RoleProvisioners;

/// <summary>
/// Provisions users with the tenant Admin role.
/// </summary>
public sealed class AdminRoleProvisioner(IUserFactory userFactory) : IUserRoleProvisioner
{
    /// <inheritdoc />
    public string RoleName => RoleNames.Admin;

    /// <inheritdoc />
    public bool RequiresTemplateIds => false;

    /// <inheritdoc />
    public User CreateUser(RoleAssignmentRequest request) =>
        userFactory.CreateAdmin(
            new UserId(Guid.NewGuid()),
            request.Name,
            request.Email,
            request.GrantedBy,
            request.GrantedOn);

    /// <inheritdoc />
    public void AssignToExistingUser(User user, RoleAssignmentRequest request) =>
        userFactory.GrantAdminAccess(user, request.GrantedBy, request.GrantedOn);
}
