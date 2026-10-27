using GovUK.Dfe.FlexForms.Utils.Diagnostics;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Common.Behaviours;

/// <summary>
/// Adds each MediatR request to the request timings as "handler {RequestName}", including requests sent from other handlers.
/// </summary>
public sealed class RequestTimingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string StepName = $"handler {typeof(TRequest).Name}";

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        using (RequestTimings.Measure(StepName))
            return await next();
    }
}
