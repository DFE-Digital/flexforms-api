using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetTenantSettingCategoryCookbookQuery
    : IRequest<Result<GetTenantSettingCategoryCookbookResponse>>;

public sealed class GetTenantSettingCategoryCookbookQueryHandler(
    IPermissionCheckerService permissionChecker)
    : IRequestHandler<GetTenantSettingCategoryCookbookQuery, Result<GetTenantSettingCategoryCookbookResponse>>
{
    public Task<Result<GetTenantSettingCategoryCookbookResponse>> Handle(
        GetTenantSettingCategoryCookbookQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Task.FromResult(Result<GetTenantSettingCategoryCookbookResponse>.Forbid(
                "Only interactive SuperAdmin users can view the category cookbook."));
        }

        var entries = TenantSettingCategoryCookbook.All;
        return Task.FromResult(Result<GetTenantSettingCategoryCookbookResponse>.Success(
            new GetTenantSettingCategoryCookbookResponse(entries)));
    }
}
