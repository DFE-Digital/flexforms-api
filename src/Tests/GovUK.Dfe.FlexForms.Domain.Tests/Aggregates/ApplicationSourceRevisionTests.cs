using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Aggregates;

public class ApplicationSourceRevisionTests
{
    private static readonly UserId User = new(Guid.NewGuid());

    private static Domain.Entities.Application NewApplication()
    {
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(new ApplicationId(Guid.NewGuid()), "APP-1", templateVersionId, DateTime.UtcNow, User);
        var templateVersion = new TemplateVersion(templateVersionId, new TemplateId(Guid.NewGuid()), "1.0", "{}", DateTime.UtcNow, User);
        typeof(Domain.Entities.Application).GetProperty(nameof(Domain.Entities.Application.TemplateVersion))!.SetValue(application, templateVersion);
        return application;
    }

    private static ApplicationResponse NewResponse(Domain.Entities.Application application) =>
        new(new ResponseId(Guid.NewGuid()), application.Id!, "{}", DateTime.UtcNow, User);

    [Fact]
    public void New_application_starts_at_revision_zero()
    {
        var application = NewApplication();

        Assert.Equal(0, application.SourceRevision);
        Assert.Null(application.SubmittedRevision);
    }

    [Fact]
    public void Each_response_increments_revision_and_is_stamped_with_it()
    {
        var application = NewApplication();
        var first = NewResponse(application);
        var second = NewResponse(application);

        application.AddResponse(first);
        application.AddResponse(second);

        Assert.Equal(2, application.SourceRevision);
        Assert.Equal(1, first.CreatedAtRevision);
        Assert.Equal(2, second.CreatedAtRevision);
    }

    [Fact]
    public void Submit_increments_revision_and_records_submitted_revision()
    {
        var application = NewApplication();
        application.AddResponse(NewResponse(application));

        application.Submit(DateTime.UtcNow, User, "user@example.com", "User");

        Assert.Equal(2, application.SourceRevision);
        Assert.Equal(2, application.SubmittedRevision);
    }

    [Fact]
    public void Delete_increments_revision_and_keeps_submitted_revision()
    {
        var application = NewApplication();
        application.AddResponse(NewResponse(application));
        application.Submit(DateTime.UtcNow, User, "user@example.com", "User");

        application.Delete(DateTime.UtcNow, User, "user@example.com", "User");

        Assert.Equal(3, application.SourceRevision);
        Assert.Equal(2, application.SubmittedRevision);
    }

    [Fact]
    public void CreatedAtRevision_is_immutable_once_assigned()
    {
        var response = new ApplicationResponse(new ResponseId(Guid.NewGuid()), new ApplicationId(Guid.NewGuid()), "{}", DateTime.UtcNow, User);
        response.AssignCreatedAtRevision(3);

        response.AssignCreatedAtRevision(3);
        Assert.Throws<InvalidOperationException>(() => response.AssignCreatedAtRevision(4));
    }

    [Fact]
    public void CreatedAtRevision_must_be_positive()
    {
        var response = new ApplicationResponse(new ResponseId(Guid.NewGuid()), new ApplicationId(Guid.NewGuid()), "{}", DateTime.UtcNow, User);

        Assert.Throws<ArgumentOutOfRangeException>(() => response.AssignCreatedAtRevision(0));
    }
}
