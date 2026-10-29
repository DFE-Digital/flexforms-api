using System.Text.Json;
using GovUK.Dfe.FlexForms.Domain.Common;

namespace GovUK.Dfe.FlexForms.Application.Applications;

/// <summary>
/// Reads the optional contributor role from a template schema and decides what access an invite grants.
/// </summary>
public static class ContributorInviteAccess
{
    public const string RoleNotFound = "The contributor role on this template was not found.";
    public const string RoleNotAllowed = "That role cannot be given to invited contributors.";

    public static bool TryReadRoleId(string? jsonSchema, out Guid roleId)
    {
        roleId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(jsonSchema))
            return false;

        try
        {
            using var document = JsonDocument.Parse(jsonSchema);
            if (!document.RootElement.TryGetProperty("contributorRoleId", out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return Guid.TryParse(property.GetString(), out roleId) && roleId != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static ContributorInviteDecision Decide(Guid? roleId, string? roleName)
    {
        if (roleId is null || roleId == Guid.Empty || string.IsNullOrWhiteSpace(roleName))
            throw new InvalidOperationException(RoleNotFound);

        if (RoleNames.IsSuperAdmin(roleName)
            || string.Equals(roleName, RoleNames.Admin, StringComparison.OrdinalIgnoreCase)
            || roleId == RoleConstants.AdminRoleId)
        {
            throw new InvalidOperationException(RoleNotAllowed);
        }

        var isUser = roleId == RoleConstants.UserRoleId
                     || string.Equals(roleName, RoleNames.User, StringComparison.OrdinalIgnoreCase);

        return new ContributorInviteDecision(
            ApplicationReadOnly: !isUser,
            MembershipRoleName: isUser ? RoleNames.User : roleName.Trim());
    }
}

public sealed record ContributorInviteDecision(bool ApplicationReadOnly, string MembershipRoleName);
