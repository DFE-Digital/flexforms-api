using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Templates.Commands
{
    /// <summary>
    /// Command to create or update a custom application status for a template.
    /// If the status exists (by TemplateId and ApplicationStatus), it will be updated.
    /// Otherwise, a new one will be created.
    /// </summary>
    public sealed record UpdateCustomApplicationStatusCommand(
        Guid TemplateId,
        ApplicationStatus? ApplicationStatus,
        string? Label) : IRequest<Result<CustomApplicationStatusDto>>;

    public sealed class UpdateCustomApplicationStatusCommandHandler(
        IEaRepository<CustomApplicationStatus> customApplicationStatusRepo,
        IEaRepository<User> userRepo,
        IHttpContextAccessor httpContextAccessor,
        ITenantTemplateResolver tenantTemplateResolver,
        IUnitOfWork unitOfWork)
        : IRequestHandler<UpdateCustomApplicationStatusCommand, Result<CustomApplicationStatusDto>>
    {
        public async Task<Result<CustomApplicationStatusDto>> Handle(UpdateCustomApplicationStatusCommand request, CancellationToken cancellationToken)
        {
            try
            {            
                var httpContext = httpContextAccessor.HttpContext;
                if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                    return Result<CustomApplicationStatusDto>.Forbid("Not authenticated");

                var principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");
                if (string.IsNullOrEmpty(principalId))
                    principalId = user.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(principalId))
                    return Result<CustomApplicationStatusDto>.Forbid("No user identifier");

                User? dbUser;
                if (principalId.Contains('@'))
                {
                    dbUser = await new GovUK.Dfe.FlexForms.Application.Users.QueryObjects.GetUserByEmailQueryObject(principalId)
                        .Apply(userRepo.Query().AsNoTracking())
                        .FirstOrDefaultAsync(cancellationToken);
                }
                else
                {
                    dbUser = await new GovUK.Dfe.FlexForms.Application.Users.QueryObjects.GetUserByExternalProviderIdQueryObject(principalId)
                        .Apply(userRepo.Query().AsNoTracking())
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (dbUser is null || dbUser.Id is null)
                    return Result<CustomApplicationStatusDto>.Forbid("Unable to resolve CreatedBy user");

                var templateId = new TemplateId(request.TemplateId);
                if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))
                {
                    return Result<CustomApplicationStatusDto>.NotFound("Template not found");
                }

                var createdByUserId = dbUser.Id;

                // Check if custom status exists for this template and application status
                var existing = await new GetCustomApplicationStatusByTemplateIdAndApplicationStatusQueryObject(
                        request.TemplateId,
                        request.ApplicationStatus!.Value)
                    .Apply(customApplicationStatusRepo.Query())
                    .FirstOrDefaultAsync(cancellationToken);

                if (existing is not null)
                {
                    existing.UpdateLabel(request.Label);
                    await unitOfWork.CommitAsync(cancellationToken);

                    return Result<CustomApplicationStatusDto>.Success(MapToDto(existing));
                }

                var entity = new CustomApplicationStatus(
                    new CustomApplicationStatusId(Guid.NewGuid()),
                    new TemplateId(request.TemplateId),
                    request.ApplicationStatus!.Value,
                    request.Label,
                    DateTime.UtcNow,
                    createdByUserId);

                await customApplicationStatusRepo.AddAsync(entity, cancellationToken);
                await unitOfWork.CommitAsync(cancellationToken);

                return Result<CustomApplicationStatusDto>.Success(MapToDto(entity));
            }
            catch (Exception e)
            {
                return Result<CustomApplicationStatusDto>.Failure(e.ToString());
            }
        }

        private static CustomApplicationStatusDto MapToDto(CustomApplicationStatus entity) =>
            new()
            {
                CustomApplicationStatusId = entity.Id!.Value,
                TemplateId = entity.TemplateId.Value,
                ApplicationStatus = entity.ApplicationStatus,
                Label = entity.Label,
                CreatedOn = entity.CreatedOn,
                CreatedBy = entity.CreatedBy.Value
            };
    }
}
