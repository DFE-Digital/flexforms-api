using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.Commands;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MockQueryable.NSubstitute;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Templates;

public class SetTemplateLiveCommandHandlerTests
{
    private readonly IEaRepository<Template> _templates = Substitute.For<IEaRepository<Template>>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantTemplateResolver _tenantTemplates = Substitute.For<ITenantTemplateResolver>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Template _template;
    private readonly SetTemplateLiveCommandHandler _handler;

    public SetTemplateLiveCommandHandlerTests()
    {
        _template = new Template(new TemplateId(Guid.NewGuid()), "Visits", DateTime.UtcNow, new UserId(Guid.NewGuid()));
        var templates = new[] { _template }.AsQueryable().BuildMockDbSet();
        _templates.Query().Returns(templates);
        _tenantTemplates.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>()).Returns(true);
        _permissions.CanManageTemplates().Returns(true);
        _handler = new SetTemplateLiveCommandHandler(_templates, _permissions, _tenantTemplates, _unitOfWork);
    }

    [Fact]
    public async Task Handle_SetsLive_WhenTheManagerHasAccessToTheTemplate()
    {
        _permissions.HasPermission(ResourceType.Template, _template.Id!.Value.ToString(), AccessType.Write).Returns(true);

        var result = await _handler.Handle(new SetTemplateLiveCommand(_template.Id.Value, true), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(_template.IsLive);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Forbids_WhenTheManagerHasNoAccessToTheTemplate()
    {
        var result = await _handler.Handle(new SetTemplateLiveCommand(_template.Id!.Value, true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("User does not have access to this template", result.Error);
        Assert.False(_template.IsLive);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }
}
