using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetTenantSettingCategoryCookbookQuery
    : IRequest<Result<GetTenantSettingCategoryCookbookResponse>>;

/// <summary>
/// Returns category cookbook entries for Tenant Config UI guidance.
/// SuperAdmin receives the full cookbook; Tenant Admin receives only non-SuperAdmin-only categories
/// (so Auth/ConnectionStrings/FileStorage examples are not exposed as attack surface).
/// </summary>
public sealed class GetTenantSettingCategoryCookbookQueryHandler(
    IPermissionCheckerService permissionChecker)
    : IRequestHandler<GetTenantSettingCategoryCookbookQuery, Result<GetTenantSettingCategoryCookbookResponse>>
{
    public Task<Result<GetTenantSettingCategoryCookbookResponse>> Handle(
        GetTenantSettingCategoryCookbookQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Task.FromResult(Result<GetTenantSettingCategoryCookbookResponse>.Forbid(
                "Only interactive tenant administrators can view the category cookbook."));
        }

        var entries = permissionChecker.IsInteractivePlatformAdmin()
            ? TenantSettingCategoryCookbook.All
            : TenantSettingCategoryCookbook.All
                .Where(e => !SuperAdminOnlyTenantSettingCategories.IsRestricted(e.Category))
                .ToList()
                .AsReadOnly();

        return Task.FromResult(Result<GetTenantSettingCategoryCookbookResponse>.Success(
            new GetTenantSettingCategoryCookbookResponse(entries)));
    }
}
