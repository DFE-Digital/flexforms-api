using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Application.Services;

public interface IFileValidationModeResolver
{
    FileValidationMode Resolve(Guid? templateId);
}
