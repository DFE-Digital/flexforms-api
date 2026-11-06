using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace GovUK.Dfe.FlexForms.Api.Telemetry;

/// <summary>
/// Records 499 (caller closed the request, e.g. the browser navigated away and Web cancelled its call)
/// as a successful request. Application Insights treats every response code of 400 or above as a
/// failure; a client abort is not a server fault. The 499 response code is kept.
/// </summary>
public class ClientAbortedRequestTelemetryProcessor(ITelemetryProcessor next) : ITelemetryProcessor
{
    private const string ClientClosedRequest = "499";

    public void Process(ITelemetry item)
    {
        if (item is RequestTelemetry { ResponseCode: ClientClosedRequest } request)
            request.Success = true;

        next.Process(item);
    }
}
