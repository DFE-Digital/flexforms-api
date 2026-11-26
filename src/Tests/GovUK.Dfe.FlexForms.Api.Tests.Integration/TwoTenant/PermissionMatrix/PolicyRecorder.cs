using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant.PermissionMatrix;

/// <summary>What the endpoint's authorization policy decided for one request.</summary>
public enum PolicyOutcome
{
    /// <summary>No policy ran: the endpoint allows anonymous callers.</summary>
    NotEvaluated,
    Allowed,
    Challenged,
    Forbidden,
}

/// <summary>
/// Test-only wrapper around the API's <see cref="IAuthorizationMiddlewareResultHandler"/>. It records the policy
/// result for requests that carry <see cref="RequestIdHeader"/>, then hands over to the real handler, so
/// responses are unchanged. With <see cref="PolicyOnlyHeader"/> as well, an allowed request stops here with
/// 204 instead of running the endpoint, so the policy gate can be measured without side effects.
/// </summary>
public sealed class PolicyRecorder(IAuthorizationMiddlewareResultHandler inner, PolicyOutcomes outcomes)
    : IAuthorizationMiddlewareResultHandler
{
    public const string RequestIdHeader = "X-Test-Request-Id";
    public const string PolicyOnlyHeader = "X-Test-Policy-Only";

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (context.Request.Headers.TryGetValue(RequestIdHeader, out var id))
        {
            outcomes.Record(id.ToString(), result.Succeeded ? PolicyOutcome.Allowed
                : result.Challenged ? PolicyOutcome.Challenged
                : PolicyOutcome.Forbidden);

            if (result.Succeeded && context.Request.Headers.ContainsKey(PolicyOnlyHeader))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }
        }

        return inner.HandleAsync(next, context, policy, result);
    }
}

/// <summary>The recorded outcomes, by request id.</summary>
public sealed class PolicyOutcomes
{
    private readonly ConcurrentDictionary<string, PolicyOutcome> _byRequest = new();

    public void Record(string requestId, PolicyOutcome outcome) => _byRequest[requestId] = outcome;

    public PolicyOutcome Take(string requestId) =>
        _byRequest.TryRemove(requestId, out var outcome) ? outcome : PolicyOutcome.NotEvaluated;
}
