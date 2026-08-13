using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Reads the tenant <c>FileValidation</c> setting:
/// <code>
/// { "DefaultMode": "Off", "Templates": { "&lt;templateId&gt;": "RequirePassed" } }
/// </code>
/// </summary>
public sealed class FileValidationModeResolver(ITenantContextAccessor tenantContextAccessor)
    : IFileValidationModeResolver
{
    public const string SectionName = TenantSafeSettingCategories.FileValidation;

    public FileValidationMode Resolve(Guid? templateId)
    {
        var settings = tenantContextAccessor.CurrentTenant?.Settings;
        if (settings is null)
            return FileValidationMode.Off;

        var section = settings.GetSection(SectionName);
        if (!section.Exists())
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
