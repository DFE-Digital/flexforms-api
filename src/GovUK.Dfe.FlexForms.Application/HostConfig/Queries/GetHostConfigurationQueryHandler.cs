using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.HostConfig.Queries;

/// <summary>
/// Returns global host configuration for platform callers (e.g. Web startup bootstrap).
/// </summary>
public sealed record GetHostConfigurationQuery(string Target)
    : IRequest<Result<HostConfigurationDto>>;

public sealed class GetHostConfigurationQueryHandler(IHostConfigurationReader hostConfigurationReader, ILogger<GetHostConfigurationQuery> _logger)
    : IRequestHandler<GetHostConfigurationQuery, Result<HostConfigurationDto>>
{
    private static readonly HashSet<string> AllowedTargets =
        new(StringComparer.OrdinalIgnoreCase) { "Web", "Api" };

    public Task<Result<HostConfigurationDto>> Handle(
        GetHostConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Target) ||
            !AllowedTargets.Contains(request.Target.Trim()))
        {
            return Task.FromResult(Result<HostConfigurationDto>.Validation(
                $"Invalid target '{request.Target}'. Allowed values: Web, Api."));
        }

        _logger.LogInformation("Hellow");
        var normalizedTarget = request.Target.Trim();
        var snapshot = hostConfigurationReader.GetConfiguration(normalizedTarget);

        var dto = new HostConfigurationDto(
            snapshot.Target,
            snapshot.LoadedAtUtc,
            snapshot.Configuration);

        return Task.FromResult(Result<HostConfigurationDto>.Success(dto));
    }
}
