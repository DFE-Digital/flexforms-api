using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;
using GovUK.Dfe.FlexForms.Utils.File;

namespace GovUK.Dfe.FlexForms.Application.Applications.Queries;

public sealed record DownloadFileQuery(Guid FileId, ApplicationId ApplicationId) : IRequest<Result<DownloadFileResult>>;

public class DownloadFileQueryHandler(
    IEaRepository<File> uploadRepository,
    IEaRepository<User> userRepository,
    ITenantAwareFileStorageService fileStorageService,
    IEaRepository<Domain.Entities.Application> applicationRepository,
    IPermissionCheckerService permissionCheckerService,
    IHttpContextAccessor httpContextAccessor,
    ITenantPermissionFilter tenantPermissionFilter)
    : IRequestHandler<DownloadFileQuery, Result<DownloadFileResult>>
{
    public async Task<Result<DownloadFileResult>> Handle(DownloadFileQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                return Result<DownloadFileResult>.Forbid("Not authenticated");

            var principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");
            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(principalId))
                return Result<DownloadFileResult>.Forbid("No user identifier");

            User? dbUser;
            if (principalId.Contains('@'))
            {
                dbUser = await (new GetUserByEmailQueryObject(principalId))
                    .Apply(userRepository.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                dbUser = await (new GetUserByExternalProviderIdQueryObject(principalId))
                    .Apply(userRepository.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (dbUser is null)
                return Result<DownloadFileResult>.NotFound("User not found");

            var application = new GetApplicationByIdQueryObject(request.ApplicationId)
                .Apply(applicationRepository.Query())
                .FirstOrDefault();
            if (application == null)
                return Result<DownloadFileResult>.NotFound("Application not found");

            // Permission check: user must have read permission for this file
            if (!permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, application.Id!.Value.ToString(), AccessType.Read))
                return Result<DownloadFileResult>.Forbid("User does not have permission to download this file");

            if (!await tenantPermissionFilter.ApplicationBelongsToCurrentTenantAsync(request.ApplicationId.Value, cancellationToken))
            {
                return Result<DownloadFileResult>.NotFound("Application not found");
            }

            var upload = new GetFileByIdQueryObject(new FileId(request.FileId))
                .Apply(uploadRepository.Query())
                .FirstOrDefault();
            if (upload == null)
                return Result<DownloadFileResult>.NotFound("File not found");

            var storagePath = $"{upload.Path}/{upload.FileName}";
            var fileStream = await fileStorageService.DownloadAsync(storagePath, cancellationToken);

            // Infer content type from file extension (simple approach)

            return Result<DownloadFileResult>.Success(new DownloadFileResult
            {
                FileStream = fileStream,
                FileName = upload.OriginalFileName,
                ContentType = upload.OriginalFileName.GetContentType()
            });
        }
        catch (Exception e)
        {
            return Result<DownloadFileResult>.Failure(e.Message);
        }
    }
} 
