using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

[RateLimit(10, 10)]
public sealed record RecordFileValidationResultCommand(
    Guid FileId,
    bool IsValid,
    string? Message,
    string? CorrelationId,
    string? Source) : IRequest<Result<UploadDto>>, IRateLimitedRequest;
