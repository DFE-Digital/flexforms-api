using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

public sealed record GetTenantAccessAuditLogQuery(int Take = 100)
    : IRequest<Result<GetTenantAccessAuditLogDto>>;

internal class GetTenantAccessAuditLogQueryValidator : AbstractValidator<GetTenantAccessAuditLogQuery>
{
    public GetTenantAccessAuditLogQueryValidator()
    {
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class GetTenantAccessAuditLogQueryHandler(
    ITenantAccessAuditQuery auditQuery,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker)
    : IRequestHandler<GetTenantAccessAuditLogQuery, Result<GetTenantAccessAuditLogDto>>
{
    public async Task<Result<GetTenantAccessAuditLogDto>> Handle(
        GetTenantAccessAuditLogQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.CanManageUsers())
        {
            return Result<GetTenantAccessAuditLogDto>.Forbid(
                "Only administrators can view the user access audit trail.");
        }

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
        {
            return Result<GetTenantAccessAuditLogDto>.Forbid("Tenant context is required.");
        }

        var rows = await auditQuery.ListAsync(tenant.Id, request.Take, cancellationToken);
        var entries = rows.Select(a => new TenantAccessAuditEntryDto(
            a.Id,
            a.TenantId,
            a.SubjectUserId,
            a.SubjectEmail,
            a.Action,
            a.RoleName,
            a.ActorUserId,
            a.ActorEmail,
            a.Details,
            a.OccurredAtUtc)).ToList();

        return Result<GetTenantAccessAuditLogDto>.Success(
            new GetTenantAccessAuditLogDto(tenant.Id, entries));
    }
}
