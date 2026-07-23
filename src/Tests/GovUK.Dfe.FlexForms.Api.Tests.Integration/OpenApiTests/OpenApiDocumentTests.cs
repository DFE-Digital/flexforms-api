using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using System.Net;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.OpenApiTests;

public class OpenApiDocumentTests
{
#pragma warning disable xUnit1026

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task SwaggerEndpoint_ReturnsSuccessAndCorrectContentType(
        CustomWebApplicationDbContextFactory<Program> factory,
        HttpClient client)
    {
        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
#pragma warning restore xUnit1026
}
