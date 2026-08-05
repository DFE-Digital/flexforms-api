using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Preset custom-role templates for tenant Role Manager (Caseworker, Reviewer).
/// Creates non-system roles with recommended <see cref="RolePermission"/> grants.
/// </summary>
public static class RoleTemplates
{
    public const string CaseworkerKey = "Caseworker";
    public const string ReviewerKey = "Reviewer";

    public sealed record Grant(ResourceType ResourceType, string ResourceKey, AccessType AccessType);

    public sealed record Template(
        string Key,
        string DefaultRoleName,
        string Description,
        IReadOnlyList<Grant> Grants);

    public static readonly IReadOnlyList<Template> All =
    [
        new Template(
            CaseworkerKey,
            "Caseworker",
            "Read all applications and files; create applications on any template.",
            [
                new Grant(ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read),
                new Grant(ResourceType.ApplicationFiles, PermissionConstants.AnyResourceKey, AccessType.Read),
                new Grant(ResourceType.Template, PermissionConstants.AnyResourceKey, AccessType.Write)
            ]),

        new Template(
            ReviewerKey,
            "Reviewer",
            "Read-only access to all applications and files in the tenant.",
            [
                new Grant(ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read),
                new Grant(ResourceType.ApplicationFiles, PermissionConstants.AnyResourceKey, AccessType.Read)
            ])
    ];

    public static Template? Get(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return All.FirstOrDefault(t =>
            string.Equals(t.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
