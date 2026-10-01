using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Prism.Queries;

/// <summary>
/// One immutable response version, used by Prism to project the exact submitted response.
/// </summary>
public sealed record GetPrismResponseQuery(Guid ResponseId) : IRequest<Result<PrismResponseDto>>;

public sealed class GetPrismResponseQueryHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    ITenantTemplateResolver tenantTemplateResolver)
    : IRequestHandler<GetPrismResponseQuery, Result<PrismResponseDto>>
{
    public async Task<Result<PrismResponseDto>> Handle(
        GetPrismResponseQuery request,
        CancellationToken cancellationToken)
    {
        var responseId = new ResponseId(request.ResponseId);

        var response = await applicationRepo.Query()
            .AsNoTracking()
            .SelectMany(a => a.Responses, (a, r) => new
            {
                r.Id,
                r.ApplicationId,
                r.CreatedAtRevision,
                r.CreatedOn,
                r.ResponseBody,
                a.TemplateVersion!.TemplateId
            })
            .Where(x => x.Id == responseId)
            .FirstOrDefaultAsync(cancellationToken);

        if (response is null
            || !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(response.TemplateId, cancellationToken))
        {
            return Result<PrismResponseDto>.NotFound("Response not found");
        }

        return Result<PrismResponseDto>.Success(new PrismResponseDto(
            request.ResponseId,
            response.ApplicationId.Value,
            response.CreatedAtRevision,
            response.CreatedOn,
            response.ResponseBody));
    }
}
