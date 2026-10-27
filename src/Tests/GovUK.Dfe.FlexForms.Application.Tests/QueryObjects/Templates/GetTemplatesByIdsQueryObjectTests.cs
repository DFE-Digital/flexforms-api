using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Templates;

public class GetTemplatesByIdsQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnMatchingTemplates_OrderedByName()
    {
        var userId = new UserId(Guid.NewGuid());
        var idA = new TemplateId(Guid.NewGuid());
        var idB = new TemplateId(Guid.NewGuid());
        var idC = new TemplateId(Guid.NewGuid());

        var templates = new List<Template>
        {
            new(idB, "Beta", DateTime.UtcNow, userId),
            new(idA, "Alpha", DateTime.UtcNow, userId),
            new(idC, "Gamma", DateTime.UtcNow, userId)
        };

        var queryObject = new GetTemplatesByIdsQueryObject(new[] { idA, idB });
        var result = queryObject.Apply(templates.AsQueryable().BuildMock()).ToList();

        result.Should().HaveCount(2);
        result.Select(t => t.Name).Should().ContainInOrder("Alpha", "Beta");
        result.Should().NotContain(t => t.Id == idC);
    }

    [Fact]
    public void Apply_ShouldReturnEmpty_WhenNoIdsProvided()
    {
        var userId = new UserId(Guid.NewGuid());
        var templates = new List<Template>
        {
            new(new TemplateId(Guid.NewGuid()), "Alpha", DateTime.UtcNow, userId)
        };

        var queryObject = new GetTemplatesByIdsQueryObject(Array.Empty<TemplateId>());
        var result = queryObject.Apply(templates.AsQueryable().BuildMock()).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ShouldNotIncludeTemplateVersions()
    {
        var templateId = new TemplateId(Guid.NewGuid());

        var query = new GetTemplatesByIdsQueryObject(new[] { templateId })
            .Apply(new List<Template>().AsQueryable());

        query.Expression.ToString().Should().NotContain("Include");
    }
}

public class GetAllTemplateIdsQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnAllTemplateIds()
    {
        var userId = new UserId(Guid.NewGuid());
        var idA = new TemplateId(Guid.NewGuid());
        var idB = new TemplateId(Guid.NewGuid());

        var templates = new List<Template>
        {
            new(idA, "Alpha", DateTime.UtcNow, userId),
            new(idB, "Beta", DateTime.UtcNow, userId)
        };

        var result = new GetAllTemplateIdsQueryObject()
            .Apply(templates.AsQueryable().BuildMock())
            .ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(idA);
        result.Should().Contain(idB);
    }

    [Fact]
    public void Apply_ShouldReturnEmpty_WhenNoTemplatesExist()
    {
        var result = new GetAllTemplateIdsQueryObject()
            .Apply(new List<Template>().AsQueryable().BuildMock())
            .ToList();

        result.Should().BeEmpty();
    }
}
