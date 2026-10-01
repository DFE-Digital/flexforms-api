using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Prism.Queries;

/// <summary>
/// An immutable template version schema by id. Prism caches the result indefinitely.
/// </summary>
public sealed record GetPrismTemplateVersionQuery(Guid TemplateVersionId) : IRequest<Result<PrismTemplateVersionDto>>;

public sealed class GetPrismTemplateVersionQueryHandler(
    IEaRepository<TemplateVersion> templateVersionRepo,
    ITenantTemplateResolver tenantTemplateResolver)
    : IRequestHandler<GetPrismTemplateVersionQuery, Result<PrismTemplateVersionDto>>
{
    public async Task<Result<PrismTemplateVersionDto>> Handle(
        GetPrismTemplateVersionQuery request,
        CancellationToken cancellationToken)
    {
        var templateVersionId = new TemplateVersionId(request.TemplateVersionId);

        var version = await templateVersionRepo.Query()
            .AsNoTracking()
            .Where(v => v.Id == templateVersionId)
            .Select(v => new { v.TemplateId, v.VersionNumber, v.JsonSchema, v.CreatedOn })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null
            || !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(version.TemplateId, cancellationToken))
        {
            return Result<PrismTemplateVersionDto>.NotFound("Template version not found");
        }

        return Result<PrismTemplateVersionDto>.Success(new PrismTemplateVersionDto(
            request.TemplateVersionId,
            version.TemplateId.Value,
            version.VersionNumber,
            version.JsonSchema,
            version.CreatedOn));
    }
}
