using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.NSubstitute;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin;

public class TemplateHostMappingOwnershipValidatorTests
{
    [Fact]
    public async Task ValidateAsync_RejectsForeignTenantTemplate()
    {
        var tenantId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var createdBy = new UserId(Guid.NewGuid());
        var templates = new List<Template>
        {
            new(new TemplateId(foreignId), "Foreign", DateTime.UtcNow, createdBy, tenantId: Guid.NewGuid())
        }.AsQueryable().BuildMockDbSet();

        var repo = Substitute.For<IEaRepository<Template>>();
        repo.Query().Returns(templates);

        var validator = new TemplateHostMappingOwnershipValidator(
            repo,
            NullLogger<TemplateHostMappingOwnershipValidator>.Instance);

        var errors = await validator.ValidateAsync(
            tenantId,
            "ApplicationTemplates",
            "{\"HostMappings\":{\"x\":\"" + foreignId + "\"}}");

        Assert.Contains(errors, e => e.Contains("belongs to another tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_AllowsLegacyNullTenantId()
    {
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var createdBy = new UserId(Guid.NewGuid());
        var templates = new List<Template>
        {
            new(new TemplateId(templateId), "Legacy", DateTime.UtcNow, createdBy)
        }.AsQueryable().BuildMockDbSet();

        var repo = Substitute.For<IEaRepository<Template>>();
        repo.Query().Returns(templates);

        var validator = new TemplateHostMappingOwnershipValidator(
            repo,
            NullLogger<TemplateHostMappingOwnershipValidator>.Instance);

        var errors = await validator.ValidateAsync(
            tenantId,
            "ApplicationTemplates",
            "{\"HostMappings\":{\"x\":\"" + templateId + "\"}}");

        Assert.Empty(errors);
    }
}
