using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Domain.Services;

public interface IApplicationFileValidationPolicy
{
    FileValidationGateResult Evaluate(FileValidationMode mode, IReadOnlyCollection<File> files);
}
