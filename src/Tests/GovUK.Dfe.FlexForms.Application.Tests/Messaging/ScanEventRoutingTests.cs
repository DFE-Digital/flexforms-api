using System.Text.Json;
using GovUK.Dfe.FlexForms.Application.Messaging;

namespace GovUK.Dfe.FlexForms.Application.Tests.Messaging;

public class ScanEventRoutingTests
{
    [Fact]
    public void GetMetadata_ShouldReadStringAndJsonElement()
    {
        var metadata = new Dictionary<string, object>
        {
            ["TenantId"] = "11111111-1111-4111-8111-111111111111",
            ["templateId"] = JsonSerializer.SerializeToElement("22222222-2222-4222-8222-222222222222")
        };

        Assert.Equal(
            "11111111-1111-4111-8111-111111111111",
            ScanEventRouting.GetMetadata(metadata, ScanEventRouting.TenantIdMetadata));
        Assert.Equal(
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            ScanEventRouting.GetMetadataGuid(metadata, ScanEventRouting.TemplateIdMetadata));
    }

    [Fact]
    public void GetMetadata_ShouldBeCaseInsensitive()
    {
        var metadata = new Dictionary<string, object>
        {
            ["userid"] = Guid.Parse("33333333-3333-4333-8333-333333333333")
        };

        Assert.Equal(
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            ScanEventRouting.GetMetadataGuid(metadata, ScanEventRouting.UserIdMetadata));
    }
}
