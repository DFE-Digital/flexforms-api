using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.Commands;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Templates;

public class CreateTemplateCommandHandlerTests
{
    private readonly IEaRepository<Template> _templateRepo = Substitute.For<IEaRepository<Template>>();
    private readonly IEaRepository<User> _userRepo = Substitute.For<IEaRepository<User>>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly ITemplateFactory _templateFactory = Substitute.For<ITemplateFactory>();
    private readonly IUserFactory _userFactory = Substitute.For<IUserFactory>();
    private readonly IUserCacheInvalidator _cacheInvalidator = Substitute.For<IUserCacheInvalidator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CreateTemplateCommandHandler _handler;

    public CreateTemplateCommandHandlerTests()
    {
        _handler = new CreateTemplateCommandHandler(
            _templateRepo,
            _userRepo,
            _httpContextAccessor,
            _permissionChecker,
            _tenantContextAccessor,
            _templateFactory,
            _userFactory,
            _cacheInvalidator,
            _unitOfWork);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerCannotManageTemplates()
    {
        _permissionChecker.CanManageTemplates().Returns(false);

        var result = await _handler.Handle(new CreateTemplateCommand("Transfers"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Admin", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldCreateTemplate_WhenCanManageTemplates()
    {
        _permissionChecker.CanManageTemplates().Returns(true);
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(
            tenantId,
            "Transfers",
            new ConfigurationBuilder().Build(),
            []));

        var userId = new UserId(Guid.NewGuid());
        var email = "admin@education.gov.uk";
        var user = new User(userId, new RoleId(Guid.NewGuid()), "Admin", email, DateTime.UtcNow, null, null, null);

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Email, email) },
            authenticationType: "Bearer")));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var users = new List<User> { user }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var templates = new List<Template>().AsQueryable().BuildMockDbSet();
        _templateRepo.Query().Returns(templates);

        var templateId = new TemplateId(Guid.NewGuid());
        var template = new Template(templateId, "New Template", DateTime.UtcNow, userId, tenantId: tenantId);
        _templateFactory.CreateTemplate("New Template", userId, tenantId, Arg.Any<DateTime?>()).Returns(template);

        var result = await _handler.Handle(new CreateTemplateCommand("New Template"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(templateId.Value, result.Value!.TemplateId);
        Assert.Equal("New Template", result.Value.Name);
        await _templateRepo.Received(1).AddAsync(template, Arg.Any<CancellationToken>());
        _userFactory.Received(1).EnsureUserHasTemplatePermission(user, templateId, userId, Arg.Any<DateTime?>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _cacheInvalidator.Received(1).InvalidateForUserAsync(
            email,
            Arg.Any<string?>(),
            userId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenTemplateNameAlreadyExistsInTenant()
    {
        _permissionChecker.CanManageTemplates().Returns(true);
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(
            tenantId,
            "Transfers",
            new ConfigurationBuilder().Build(),
            []));

        var userId = new UserId(Guid.NewGuid());
        var email = "admin@education.gov.uk";
        var user = new User(userId, new RoleId(Guid.NewGuid()), "Admin", email, DateTime.UtcNow, null, null, null);

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Email, email) },
            authenticationType: "Bearer")));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var users = new List<User> { user }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var existingTemplate = new Template(
            new TemplateId(Guid.NewGuid()),
            "Existing Template",
            DateTime.UtcNow,
            userId,
            tenantId: tenantId);
        var templates = new List<Template> { existingTemplate }.AsQueryable().BuildMockDbSet();
        _templateRepo.Query().Returns(templates);

        var result = await _handler.Handle(new CreateTemplateCommand("existing template"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.Error, StringComparison.OrdinalIgnoreCase);
        await _templateRepo.DidNotReceive().AddAsync(Arg.Any<Template>(), Arg.Any<CancellationToken>());
    }
}
