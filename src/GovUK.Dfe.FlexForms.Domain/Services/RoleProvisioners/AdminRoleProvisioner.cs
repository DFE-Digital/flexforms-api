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
    public User CreateUser(RoleAssignmentRequest request)
    {
        var tenantAdminRoleId = RequireTenantAdminRoleId(request);
        return userFactory.CreateAdmin(
            new UserId(Guid.NewGuid()),
            tenantAdminRoleId,
            request.Name,
            request.Email,
            request.GrantedBy,
            request.GrantedOn);
    }

    /// <inheritdoc />
    public void AssignToExistingUser(User user, RoleAssignmentRequest request)
    {
        var tenantAdminRoleId = RequireTenantAdminRoleId(request);
        userFactory.GrantAdminAccess(user, tenantAdminRoleId, request.GrantedBy, request.GrantedOn);
    }

    private static RoleId RequireTenantAdminRoleId(RoleAssignmentRequest request)
    {
        if (request.TenantRoleId is null)
        {
            throw new ArgumentException(
                "Tenant Admin role id is required when assigning the Admin role.",
                nameof(request));
        }

        return request.TenantRoleId;
    }
}
