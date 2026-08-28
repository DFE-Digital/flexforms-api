using GovUK.Dfe.FlexForms.Api.Telemetry;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Telemetry;

public class PiiMaskingTests
{
    [Fact]
    public void MaskEmail_ShouldKeepFirstTwoAndLastFiveCharacters()
    {
        var masked = PiiMasking.MaskEmail("farshad.dashti@education.gov.uk");

        Assert.Equal("fa************************ov.uk", masked);
        Assert.StartsWith("fa", masked);
        Assert.EndsWith("ov.uk", masked);
    }

    [Fact]
    public void MaskEmail_ShouldHandleShortValues()
    {
        Assert.Equal("ab*****", PiiMasking.MaskEmail("ab@x.co"));
    }

    [Fact]
    public void MaskEmailsInText_ShouldMaskEmbeddedAddresses()
    {
        var text = "Sent to farshad.dashti@education.gov.uk successfully";
        var masked = PiiMasking.MaskEmailsInText(text);

        Assert.DoesNotContain("farshad.dashti@education.gov.uk", masked);
        Assert.Contains("fa", masked);
        Assert.Contains("ov.uk", masked);
    }
}
