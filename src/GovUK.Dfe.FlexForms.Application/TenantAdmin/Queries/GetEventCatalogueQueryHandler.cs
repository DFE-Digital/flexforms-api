using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Messaging;
using GovUK.Dfe.FlexForms.Domain.Messaging;
using GovUK.Dfe.FlexForms.Domain.Services;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetEventCatalogueQuery : IRequest<Result<GetEventCatalogueResponse>>;

/// <summary>
/// Returns the platform typed-event catalogue discovered from Messaging.Contracts.
/// </summary>
public sealed class GetEventCatalogueQueryHandler(
    IPermissionCheckerService permissionChecker)
    : IRequestHandler<GetEventCatalogueQuery, Result<GetEventCatalogueResponse>>
{
    public Task<Result<GetEventCatalogueResponse>> Handle(
        GetEventCatalogueQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin()
            && !permissionChecker.IsInteractivePlatformAdmin())
        {
            return Task.FromResult(Result<GetEventCatalogueResponse>.Forbid(
                "Only interactive Admin users can view the event catalogue."));
        }

        return Task.FromResult(Result<GetEventCatalogueResponse>.Success(
            PlatformEventCatalogueBuilder.Build()));
    }
}
