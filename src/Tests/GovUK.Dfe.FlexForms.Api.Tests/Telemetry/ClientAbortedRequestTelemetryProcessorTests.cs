using GovUK.Dfe.FlexForms.Api.Telemetry;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Telemetry;

public class ClientAbortedRequestTelemetryProcessorTests
{
    [Fact]
    public void Process_ShouldMark499AsSuccess_AndLeaveOtherFailuresAlone()
    {
        var next = new List<ITelemetry>();
        var processor = new ClientAbortedRequestTelemetryProcessor(new ListProcessor(next));
        var aborted = new RequestTelemetry { ResponseCode = "499", Success = false };
        var serverError = new RequestTelemetry { ResponseCode = "500", Success = false };

        processor.Process(aborted);
        processor.Process(serverError);
        processor.Process(new TraceTelemetry("keep"));

        Assert.True(aborted.Success);
        Assert.Equal("499", aborted.ResponseCode);
        Assert.False(serverError.Success);
        Assert.Equal(3, next.Count);
    }

    private sealed class ListProcessor(List<ITelemetry> items) : ITelemetryProcessor
    {
        public void Process(ITelemetry item) => items.Add(item);
    }
}
