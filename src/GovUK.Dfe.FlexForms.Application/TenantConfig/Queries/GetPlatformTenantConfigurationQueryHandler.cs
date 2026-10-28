using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantConfig.Queries;

/// <summary>
/// Returns merged tenant configuration for a platform caller that supplies the tenant id explicitly.
/// </summary>
public sealed record GetPlatformTenantConfigurationQuery(Guid TenantId, string Target)
    : IRequest<Result<TenantConfigurationDto>>;

public sealed class GetPlatformTenantConfigurationQueryHandler(ITenantSettingsReader settingsReader)
    : IRequestHandler<GetPlatformTenantConfigurationQuery, Result<TenantConfigurationDto>>
{
    // The platform identity is shared across tenants; the Api target holds decrypted connection strings.
    private const string WebTarget = "Web";

    public async Task<Result<TenantConfigurationDto>> Handle(
        GetPlatformTenantConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return Result<TenantConfigurationDto>.Validation("Tenant id is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Target) ||
            !string.Equals(request.Target.Trim(), WebTarget, StringComparison.OrdinalIgnoreCase))
        {
            return Result<TenantConfigurationDto>.Validation(
                $"Invalid target '{request.Target}'. Allowed values: {WebTarget}.");
        }

        var snapshot = await settingsReader.GetConfigurationAsync(
            request.TenantId,
            WebTarget,
            cancellationToken);

        if (snapshot is null)
        {
            return Result<TenantConfigurationDto>.NotFound(
                $"Tenant '{request.TenantId}' is not active or no longer exists.");
        }

        var configuration = snapshot.Configuration
            .Where(pair => !pair.Key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return Result<TenantConfigurationDto>.Success(new TenantConfigurationDto(
            snapshot.TenantId,
            snapshot.TenantName,
            WebTarget,
            snapshot.LoadedAtUtc,
            configuration));
    }
}
