using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Reads the tenant <c>FileValidation</c> setting:
/// <code>
/// {
///   "DefaultMode": "Off",
///   "Extensions": [ ".xlsx", ".xls" ],
///   "Templates": { "&lt;templateId&gt;": "RequirePassed" }
/// }
/// </code>
/// Missing or empty <c>Extensions</c> means every uploaded file is eligible when mode is not Off.
/// </summary>
public sealed class FileValidationModeResolver(ITenantContextAccessor tenantContextAccessor)
    : IFileValidationModeResolver
{
    public const string SectionName = TenantSafeSettingCategories.FileValidation;

    public FileValidationMode Resolve(Guid? templateId)
    {
        var section = GetSection();
        if (section is null)
            return FileValidationMode.Off;

        if (templateId.HasValue)
        {
            var templateValue = section.GetSection("Templates")[templateId.Value.ToString()];
            if (TryParse(templateValue, out var templateMode))
                return templateMode;
        }

        return TryParse(section["DefaultMode"] ?? section["defaultMode"], out var defaultMode)
            ? defaultMode
            : FileValidationMode.Off;
    }

    public bool IsExtensionSubjectToValidation(string? originalFileName)
    {
        var allowed = GetAllowedExtensions();
        if (allowed.Count == 0)
            return true;

        var extension = NormalizeExtension(Path.GetExtension(originalFileName ?? string.Empty));
        return extension is not null && allowed.Contains(extension);
    }

    private IConfigurationSection? GetSection()
    {
        var settings = tenantContextAccessor.CurrentTenant?.Settings;
        if (settings is null)
            return null;

        var section = settings.GetSection(SectionName);
        return section.Exists() ? section : null;
    }

    private HashSet<string> GetAllowedExtensions()
    {
        var section = GetSection();
        if (section is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetSection("Extensions").GetChildren())
        {
            var normalized = NormalizeExtension(child.Value);
            if (normalized is not null)
                allowed.Add(normalized);
        }

        return allowed;
    }

    internal static string? NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('*'))
            trimmed = trimmed.TrimStart('*');

        if (!trimmed.StartsWith('.'))
            trimmed = "." + trimmed;

        return trimmed.Length > 1 ? trimmed.ToLowerInvariant() : null;
    }

    private static bool TryParse(string? value, out FileValidationMode mode)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out mode)
            && Enum.IsDefined(mode))
        {
            return true;
        }

        mode = FileValidationMode.Off;
        return false;
    }
}
