using System.Security.Claims;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

public class UserFeedbackControllerTests
{
    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task PostAsync_should_return_202_Accepted_for_valid_request(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUserFeedbackClient userFeedbackClient)
    {
        factory.TestClaims =
        [
            new Claim(ClaimTypes.Email, EaContextSeeder.BobEmail)
        ];

        var templateId = new Guid(EaContextSeeder.TemplateId);
        var request = new BugReport("Some message", "ABC-20001231-001", "some.email@education.gov.uk", templateId);

        await userFeedbackClient.PostAsync(request);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryCustomization))]
    public async Task PostAsync_should_return_400_Bad_Request_for_invalid_data(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUserFeedbackClient userFeedbackClient)
    {
        factory.TestClaims =
        [
            new Claim(ClaimTypes.Email, EaContextSeeder.BobEmail)
        ];

        var request = new SupportRequest("", "ABC-20001231-001", "not-an-email-address", new Guid(EaContextSeeder.TemplateId));

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            userFeedbackClient.PostAsync(request));

        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [CustomAutoData(typeof(CustomWebApplicationDbContextFactoryWithRateLimitingCustomization))]
    public async Task PostAsync_should_return_429_Too_Many_Requests_when_rate_limit_exceeded(
        CustomWebApplicationDbContextFactory<Program> factory,
        IUserFeedbackClient userFeedbackClient)
    {
        factory.TestClaims =
        [
            new Claim(ClaimTypes.Email, EaContextSeeder.BobEmail)
        ];

        var templateId = new Guid(EaContextSeeder.TemplateId);
        var request1 = new BugReport("Some message 1", "ABC-20001231-001", "some.email@education.gov.uk",
            templateId);
        var request2 = new SupportRequest("Some message 2", "ABC-20001231-001", "another.email@education.gov.uk",
            templateId);

        await userFeedbackClient.PostAsync(request1);

        var ex = await Assert.ThrowsAsync<ExternalApplicationsException<ExceptionResponse>>(() =>
            userFeedbackClient.PostAsync(request2));

        Assert.Equal(429, ex.StatusCode);
        Assert.Contains("Too many requests", ex.Result?.Message ?? "");
    }
}