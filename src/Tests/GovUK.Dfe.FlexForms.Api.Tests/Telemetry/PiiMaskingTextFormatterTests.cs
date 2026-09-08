using GovUK.Dfe.FlexForms.Api.Telemetry;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Telemetry;

public class PiiMaskingTextFormatterTests
{
    private const string FullEmail = "farshad.dashti@education.gov.uk";

    [Fact]
    public void Format_ShouldMaskEmailInMessage()
    {
        var output = Format($"Sent to {FullEmail} successfully");

        Assert.DoesNotContain(FullEmail, output);
        Assert.Contains("fa", output);
        Assert.Contains("ov.uk", output);
    }

    [Fact]
    public void Format_ShouldMaskEmailInException()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            new InvalidOperationException($"Failed for {FullEmail}"),
            new MessageTemplateParser().Parse("Request failed"),
            []);

        using var writer = new StringWriter();
        new PiiMaskingTextFormatter().Format(logEvent, writer);
        var output = writer.ToString();

        Assert.DoesNotContain(FullEmail, output);
        Assert.Contains("Request failed", output);
    }

    private static string Format(string message)
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse(message),
            []);

        using var writer = new StringWriter();
        new PiiMaskingTextFormatter().Format(logEvent, writer);
        return writer.ToString();
    }
}
