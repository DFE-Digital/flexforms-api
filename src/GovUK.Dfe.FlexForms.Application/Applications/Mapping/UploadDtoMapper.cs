using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Application.Applications.Mapping;

internal static class UploadDtoMapper
{
    public static UploadDto FromFile(File file) => new()
    {
        Id = file.Id!.Value,
        ApplicationId = file.ApplicationId.Value,
        UploadedBy = file.UploadedBy.Value,
        Name = file.Name,
        Description = file.Description,
        OriginalFileName = file.OriginalFileName,
        FileName = file.FileName,
        FileSize = file.FileSize,
        UploadedOn = file.UploadedOn,
        ValidationStatus = file.ValidationStatus,
        ValidationMessage = file.ValidationMessage,
        ValidatedOn = file.ValidatedOn
    };
}
