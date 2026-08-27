using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Applications.Mapping;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GovUK.Dfe.FlexForms.Utils.File;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.CoreLibs.Caching.Helpers;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

[RateLimit(5, 10)]
public sealed record UploadFileCommand(
    ApplicationId ApplicationId,
    string Name,
    string? Description,
    string OriginalFileName,
    Stream FileContent
) : IRequest<Result<UploadDto>>, IRateLimitedRequest;

public class UploadFileCommandHandler(
    IEaRepository<File> uploadRepository,
    IEaRepository<Domain.Entities.Application> applicationRepository,
    IEaRepository<User> userRepository,
    IUnitOfWork unitOfWork,
    ITenantAwareFileStorageService fileStorageService,
    IFileFactory fileFactory,
    IHttpContextAccessor httpContextAccessor,
    IPermissionCheckerService permissionCheckerService,
    IFileValidationModeResolver fileValidationModeResolver,
    ITenantPermissionFilter tenantPermissionFilter)
    : IRequestHandler<UploadFileCommand, Result<UploadDto>>
{
    public async Task<Result<UploadDto>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || user.Identity?.IsAuthenticated != true)
                return Result<UploadDto>.Forbid("Not authenticated");

            var principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");
            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(principalId))
                return Result<UploadDto>.Forbid("No user identifier");

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
                return Result<UploadDto>.NotFound("User not found");

            // Load the Application entity - needed for domain events to have access to navigation properties
            var application = await new GetApplicationByIdQueryObject(request.ApplicationId)
                .Apply(applicationRepository.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (application == null)
                return Result<UploadDto>.NotFound("Application not found");

            // Fetch latest response body for orphan check (optimized query to avoid loading full response history)
            var latestResponseBody = await new GetApplicationUploadContextByIdQueryObject(request.ApplicationId)
                .Apply(applicationRepository.Query().AsNoTracking())
                .Select(a => a.LatestResponseBody)
                .FirstOrDefaultAsync(cancellationToken);

            // Permission check: user must have write permission for this application (File resource)
            if (!permissionCheckerService.HasPermission(ResourceType.ApplicationFiles, request.ApplicationId.Value.ToString(), AccessType.Write))
                return Result<UploadDto>.Forbid("User does not have permission to upload files for this application");

            if (!await tenantPermissionFilter.ApplicationBelongsToCurrentTenantAsync(request.ApplicationId.Value, cancellationToken))
            {
                return Result<UploadDto>.NotFound("Application not found");
            }

            // Generate hashed file name
            var hashedFileName = FileNameHasher.HashFileName(request.OriginalFileName);
            var storagePath = $"{application.ApplicationReference}/{hashedFileName}";

            var existingFile = new GetFileByFileNameApplicationIdQueryObject(hashedFileName, request.ApplicationId)
                .Apply(uploadRepository.Query())
                .FirstOrDefault();
            
            if (existingFile != null)
            {
                // Check if the existing file is orphaned (not referenced in the latest ApplicationResponse)
                var isOrphaned = string.IsNullOrEmpty(latestResponseBody) ||
                                 !latestResponseBody.Contains(
                                     existingFile.Id!.Value.ToString(),
                                     StringComparison.OrdinalIgnoreCase);

                if (isOrphaned)
                {
                    // Auto-delete the orphaned file before uploading the new one
                    var orphanedStoragePath = $"{application.ApplicationReference}/{existingFile.FileName}";
                    try
                    {
                        await fileStorageService.DeleteAsync(orphanedStoragePath, cancellationToken);
                    }
                    catch
                    {
                        // Storage deletion failure is acceptable - file may not exist on disk
                    }

                    fileFactory.DeleteFile(existingFile);
                    await uploadRepository.RemoveAsync(existingFile, cancellationToken);
                    // Don't commit yet - will commit together with the new file upload
                }
                else
                {
                    return Result<UploadDto>.Conflict("The selected file has already been uploaded. Upload a file with a different name.");
                }
            }

            // Upload file to the storage
            await fileStorageService.UploadAsync(storagePath, request.FileContent, request.OriginalFileName, cancellationToken);

            var fileSize = request.FileContent.Length;

            var fileHash = CacheKeyHelper.ComputeSha256(request.FileContent);

            // Create File entity using factory
            var upload = fileFactory.CreateUpload(
                new FileId(Guid.NewGuid()),
                application,
                request.Name,
                request.Description,
                request.OriginalFileName,
                hashedFileName,
                application.ApplicationReference,
                DateTime.UtcNow,
                dbUser.Id!,
                fileSize,
                fileHash: fileHash
            );

            var mode = fileValidationModeResolver.Resolve(application.TemplateVersion?.TemplateId.Value);
            if (mode != FileValidationMode.Off
                && fileValidationModeResolver.IsExtensionSubjectToValidation(request.OriginalFileName))
                upload.RequireExternalValidation();

            await uploadRepository.AddAsync(upload, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);

            return Result<UploadDto>.Success(UploadDtoMapper.FromFile(upload));
        }
        catch (Exception e)
        {
            return Result<UploadDto>.Failure(e.Message);
        }
    }
} 
