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
    public const string RoleInvalid = "The contributor role on this template is invalid.";

    public static bool TryReadRoleId(string? jsonSchema, out Guid roleId)
    {
        return ReadRoleId(jsonSchema, out roleId) == ContributorRoleSelection.Selected;
    }

    public static ContributorRoleSelection ReadRoleId(string? jsonSchema, out Guid roleId)
    {
        roleId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(jsonSchema))
            return ContributorRoleSelection.NotSelected;

        try
        {
            using var document = JsonDocument.Parse(jsonSchema);
            if (!document.RootElement.TryGetProperty("contributorRoleId", out var property))
                return ContributorRoleSelection.NotSelected;

            if (property.ValueKind != JsonValueKind.String)
                return ContributorRoleSelection.Invalid;

            var value = property.GetString();
            if (string.IsNullOrWhiteSpace(value))
                return ContributorRoleSelection.NotSelected;

            return Guid.TryParse(value, out roleId) && roleId != Guid.Empty
                ? ContributorRoleSelection.Selected
                : ContributorRoleSelection.Invalid;
        }
        catch (JsonException)
        {
            return ContributorRoleSelection.Invalid;
        }
    }

    public static ContributorInviteDecision Decide(Guid? roleId, string? roleName)
    {
        if (roleId is null || roleId == Guid.Empty || string.IsNullOrWhiteSpace(roleName))
            throw new InvalidOperationException(RoleNotFound);

        if (RoleNames.IsReservedRoleName(roleName)
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

public enum ContributorRoleSelection
{
    NotSelected,
    Selected,
    Invalid
}

public sealed record ContributorInviteDecision(bool ApplicationReadOnly, string MembershipRoleName);
