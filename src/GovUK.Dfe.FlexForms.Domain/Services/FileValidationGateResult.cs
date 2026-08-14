using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Domain.Services;

public sealed class FileValidationGateResult
{
    public bool CanSubmit { get; }
    public IReadOnlyList<File> BlockingFiles { get; }

    private FileValidationGateResult(bool canSubmit, IReadOnlyList<File> blockingFiles)
    {
        CanSubmit = canSubmit;
        BlockingFiles = blockingFiles;
    }

    public static FileValidationGateResult Allow() => new(true, []);

    public static FileValidationGateResult Block(IReadOnlyList<File> blockingFiles) =>
        new(false, blockingFiles);

    public string ToErrorMessage()
    {
        if (CanSubmit)
            return string.Empty;

        var names = BlockingFiles
            .Select(f => string.IsNullOrWhiteSpace(f.OriginalFileName) ? f.Name : f.OriginalFileName)
            .ToList();

        var failed = BlockingFiles.Any(f => f.ValidationStatus == FileValidationStatus.Failed);
        var pending = BlockingFiles.Any(f => f.ValidationStatus == FileValidationStatus.Pending);

        if (failed && pending)
            return $"Cannot submit while uploaded files are invalid or still being validated: {string.Join(", ", names)}.";

        if (failed)
            return $"Cannot submit because uploaded files failed validation: {string.Join(", ", names)}.";

        return $"Cannot submit until uploaded files have been validated: {string.Join(", ", names)}.";
    }
}
