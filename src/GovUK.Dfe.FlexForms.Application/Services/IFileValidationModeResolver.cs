using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Application.Services;

public interface IFileValidationModeResolver
{
    FileValidationMode Resolve(Guid? templateId);

    /// <summary>
    /// True when the file should be marked for external validation.
    /// Empty / missing <c>FileValidation:Extensions</c> means all files are eligible.
    /// </summary>
    bool IsExtensionSubjectToValidation(string? originalFileName);
}
