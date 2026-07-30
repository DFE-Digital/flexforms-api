using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Templates.Commands;

/// <summary>
/// Sets whether a template is live for end users in the current tenant (Admin only).
/// </summary>
public sealed record SetTemplateLiveCommand(Guid TemplateId, bool IsLive)
    : IRequest<Result<TemplateDto>>;

/// <summary>
/// Handles <see cref="SetTemplateLiveCommand"/>.
/// </summary>
public sealed class SetTemplateLiveCommandHandler(
    IEaRepository<Template> templateRepository,
    IPermissionCheckerService permissionCheckerService,
    ITenantTemplateResolver tenantTemplateResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SetTemplateLiveCommand, Result<TemplateDto>>
{
    public async Task<Result<TemplateDto>> Handle(
        SetTemplateLiveCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!permissionCheckerService.CanManageTemplates())
            {
                return Result<TemplateDto>.Forbid("Only Admin users or roles with Template Manage permission can set template live status.");
            }

            var templateId = new TemplateId(request.TemplateId);
            if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))
            {
                return Result<TemplateDto>.Forbid("Template does not belong to the current tenant");
            }

            var template = await new GetTemplateByIdQueryObject(templateId)
                .Apply(templateRepository.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (template is null)
            {
                return Result<TemplateDto>.NotFound("Template not found");
            }

            template.SetLive(request.IsLive);
            await unitOfWork.CommitAsync(cancellationToken);

            return Result<TemplateDto>.Success(new TemplateDto
            {
                TemplateId = template.Id!.Value,
                Name = template.Name,
                CreatedOn = template.CreatedOn,
                LatestVersionNumber = template.TemplateVersions
                    .OrderByDescending(v => v.CreatedOn)
                    .Select(v => v.VersionNumber)
                    .FirstOrDefault(),
                IsLive = template.IsLive
            });
        }
        catch (Exception ex)
        {
            return Result<TemplateDto>.Failure(ex.ToString());
        }
    }
}
