using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Domain.Services;

public sealed class ApplicationFileValidationPolicy : IApplicationFileValidationPolicy
{
    public FileValidationGateResult Evaluate(FileValidationMode mode, IReadOnlyCollection<File> files)
    {
        if (mode == FileValidationMode.Off || files.Count == 0)
            return FileValidationGateResult.Allow();

        var blocking = files
            .Where(file => IsBlocking(mode, file.ValidationStatus))
            .ToList();

        return blocking.Count == 0
            ? FileValidationGateResult.Allow()
            : FileValidationGateResult.Block(blocking);
    }

    private static bool IsBlocking(FileValidationMode mode, FileValidationStatus status) =>
        status == FileValidationStatus.Failed
        || (mode == FileValidationMode.RequirePassed && status == FileValidationStatus.Pending);
}
