using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Templates;

public class GetLiveTemplatesByIdsQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnOnlyLiveTemplatesInSet()
    {
        var userId = new UserId(Guid.NewGuid());
        var liveId = new TemplateId(Guid.NewGuid());
        var draftId = new TemplateId(Guid.NewGuid());
        var otherLiveId = new TemplateId(Guid.NewGuid());

        var templates = new List<Template>
        {
            new(liveId, "Live", DateTime.UtcNow, userId, isLive: true),
            new(draftId, "Draft", DateTime.UtcNow, userId, isLive: false),
            new(otherLiveId, "OtherLive", DateTime.UtcNow, userId, isLive: true)
        };

        var result = new GetLiveTemplatesByIdsQueryObject(new[] { liveId, draftId })
            .Apply(templates.AsQueryable().BuildMock())
            .ToList();

        result.Should().ContainSingle();
        result[0].Id.Should().Be(liveId);
        result[0].IsLive.Should().BeTrue();
    }

    [Fact]
    public void Apply_ShouldReturnEmpty_WhenNoIdsProvided()
    {
        var userId = new UserId(Guid.NewGuid());
        var templates = new List<Template>
        {
            new(new TemplateId(Guid.NewGuid()), "Live", DateTime.UtcNow, userId, isLive: true)
        };

        var result = new GetLiveTemplatesByIdsQueryObject(Array.Empty<TemplateId>())
            .Apply(templates.AsQueryable().BuildMock())
            .ToList();

        result.Should().BeEmpty();
    }
}
