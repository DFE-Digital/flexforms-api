using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetSafeTenantSettingsQuery(Guid TenantId)
    : IRequest<Result<GetTenantSettingsResponse>>;

internal class GetSafeTenantSettingsQueryValidator : AbstractValidator<GetSafeTenantSettingsQuery>
{
    public GetSafeTenantSettingsQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}

/// <summary>
/// Lists non-secret delegated organisation settings for Tenant Admins.
/// </summary>
public sealed class GetSafeTenantSettingsQueryHandler(
    ITenantSettingsQuery settingsQuery,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker)
    : IRequestHandler<GetSafeTenantSettingsQuery, Result<GetTenantSettingsResponse>>
{
    public async Task<Result<GetTenantSettingsResponse>> Handle(
        GetSafeTenantSettingsQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin()
            && !permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<GetTenantSettingsResponse>.Forbid(
                "Only interactive Admin users can view organisation settings.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            return Result<GetTenantSettingsResponse>.Forbid(
                "Tenant context is required to view organisation settings.");
        }

        if (currentTenant.Id != request.TenantId)
        {
            return Result<GetTenantSettingsResponse>.Forbid(
                $"Cannot view settings for tenant '{request.TenantId}'. " +
                $"Administrators may only view their own tenant ('{currentTenant.Id}').");
        }

        var list = await settingsQuery.ListSettingsAsync(request.TenantId, cancellationToken);
        if (list is null)
            return Result<GetTenantSettingsResponse>.NotFound($"Tenant '{request.TenantId}' was not found.");

        var safe = list.Settings
            .Where(s => TenantSafeSettingCategories.IsSafe(s.Category) && !s.IsSecret)
            .Select(s => new TenantSettingDto(
                s.SettingId,
                s.Category,
                s.Target,
                s.SettingsJson,
                s.IsSecret,
                s.UpdatedAtUtc))
            .ToList();

        return Result<GetTenantSettingsResponse>.Success(
            new GetTenantSettingsResponse(list.TenantId, list.TenantName, safe));
    }
}
