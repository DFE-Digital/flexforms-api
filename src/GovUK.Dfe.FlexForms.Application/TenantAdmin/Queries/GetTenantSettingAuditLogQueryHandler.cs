using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetTenantSettingAuditLogQuery(Guid TenantId, int Take = 100)
    : IRequest<Result<GetTenantSettingAuditLogDto>>;

public sealed class GetTenantSettingAuditLogQueryHandler(
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantSettingAuditQuery auditQuery)
    : IRequestHandler<GetTenantSettingAuditLogQuery, Result<GetTenantSettingAuditLogDto>>
{
    public async Task<Result<GetTenantSettingAuditLogDto>> Handle(
        GetTenantSettingAuditLogQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<GetTenantSettingAuditLogDto>.Forbid(
                "Only interactive tenant administrators can view tenant setting audit logs.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<GetTenantSettingAuditLogDto>.Forbid(
                "Administrators may only view audit logs for their own tenant.");
        }

        var log = await auditQuery.ListAsync(request.TenantId, request.Take, cancellationToken);
        if (log is null)
        {
            return Result<GetTenantSettingAuditLogDto>.NotFound(
                $"Tenant '{request.TenantId}' was not found.");
        }

        return Result<GetTenantSettingAuditLogDto>.Success(log);
    }
}
