using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Helpers;

internal static class ApplicationCreationServiceTestHelper
{
    public static IApplicationCreationService MockReturning(
        Domain.Entities.Application application,
        ApplicationResponse? response = null)
    {
        if (response is null)
        {
            response = new ApplicationResponse(
                new ResponseId(Guid.NewGuid()),
                application.Id!,
                "{}",
                application.CreatedOn,
                application.CreatedBy);
            application.AddResponse(response);
        }

        var service = Substitute.For<IApplicationCreationService>();
        service.CreateAsync(
                Arg.Any<TemplateVersionId>(),
                Arg.Any<string>(),
                Arg.Any<UserId>(),
                Arg.Any<CancellationToken>())
            .Returns((application, response!));
        return service;
    }
}
