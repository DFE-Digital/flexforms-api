using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

public sealed record DeleteTenantSettingCommand(
    Guid TenantId,
    string Category,
    string Target) : IRequest<Result<DeleteTenantSettingResponse>>;

public sealed class DeleteTenantSettingCommandHandler(
    ITenantSettingsWriter settingsWriter,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider,
    ITenantSettingAuditWriter auditWriter,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<DeleteTenantSettingCommand, Result<DeleteTenantSettingResponse>>
{
    public async Task<Result<DeleteTenantSettingResponse>> Handle(
        DeleteTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<DeleteTenantSettingResponse>.Forbid(
                "Only interactive tenant administrators can delete tenant settings.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<DeleteTenantSettingResponse>.Forbid(
                "Administrators may only delete settings for their own tenant.");
        }

        var category = request.Category?.Trim() ?? string.Empty;
        var target = request.Target?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(target))
        {
            return Result<DeleteTenantSettingResponse>.Validation(
                "Category and Target are required.");
        }

        if (SuperAdminOnlyTenantSettingCategories.IsRestricted(category)
            && !permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<DeleteTenantSettingResponse>.Forbid(
                $"Only SuperAdmin can delete '{category}' settings.");
        }

        try
        {
            var deleted = await settingsWriter.DeleteSettingAsync(
                request.TenantId, category, target, cancellationToken);

            if (deleted is null)
            {
                return Result<DeleteTenantSettingResponse>.NotFound(
                    $"Setting '{category}' (Target={target}) was not found.");
            }

            await auditWriter.AppendAsync(
                request.TenantId,
                category,
                target,
                "Deleted",
                ResolveActorEmail(),
                deleted.WasSecret,
                cancellationToken);

            await tenantConfigProvider.RefreshAsync(cancellationToken);

            return Result<DeleteTenantSettingResponse>.Success(
                new DeleteTenantSettingResponse(
                    request.TenantId,
                    category,
                    target,
                    $"Setting '{category}' (Target={target}) deleted successfully."));
        }
        catch (KeyNotFoundException ex)
        {
            return Result<DeleteTenantSettingResponse>.NotFound(ex.Message);
        }
    }

    private string ResolveActorEmail()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user?.FindFirst(ClaimTypes.Email)?.Value
               ?? user?.FindFirst("email")?.Value
               ?? user?.Identity?.Name
               ?? "unknown";
    }
}
