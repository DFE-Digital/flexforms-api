using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Domain.Common;
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

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

public class UpdateUserTemplateAccessCommandHandlerTests
{
    private readonly IEaRepository<User> _userRepo = Substitute.For<IEaRepository<User>>();
    private readonly IEaRepository<Template> _templateRepo = Substitute.For<IEaRepository<Template>>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IUserFactory _userFactory = Substitute.For<IUserFactory>();
    private readonly ITenantTemplateCatalogue _catalogue = Substitute.For<ITenantTemplateCatalogue>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly ITenantAccessAuditWriter _auditWriter = Substitute.For<ITenantAccessAuditWriter>();
    private readonly IUserCacheInvalidator _cacheInvalidator = Substitute.For<IUserCacheInvalidator>();
    private readonly UpdateUserTemplateAccessCommandHandler _handler;

    public UpdateUserTemplateAccessCommandHandlerTests()
    {
        _handler = new UpdateUserTemplateAccessCommandHandler(
            _userRepo,
            _templateRepo,
            _unitOfWork,
            _userFactory,
            _catalogue,
            _permissionChecker,
            _httpContextAccessor,
            _tenantContextAccessor,
            _auditWriter,
            _cacheInvalidator);
    }

    [Fact]
    public async Task Handle_ShouldAuditFormAccessUpdated_WhenTemplatesChange()
    {
        var tenantId = Guid.NewGuid();
        var templateId = new TemplateId(Guid.NewGuid());
        var admin = CreateUser("Admin", "admin@education.gov.uk", new RoleId(RoleConstants.AdminRoleId));
        var subject = CreateUser("Ada", "ada@example.com", new RoleId(RoleConstants.UserRoleId));

        _permissionChecker.CanManageUsers().Returns(true);
        _catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>()).Returns([templateId]);
        _tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(
            tenantId,
            "Transfers",
            new ConfigurationBuilder().Build(),
            []));

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, admin.Email)],
            authenticationType: "Bearer")));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var users = new[] { admin, subject }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var templates = Array.Empty<Template>().AsQueryable().BuildMockDbSet();
        _templateRepo.Query().Returns(templates);

        var result = await _handler.Handle(
            new UpdateUserTemplateAccessCommand(subject.Id!.Value, [templateId.Value]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        await _cacheInvalidator.Received(1).InvalidateForUserAsync(
            subject.Email,
            subject.ExternalProviderId,
            subject.Id!,
            Arg.Any<CancellationToken>());
        await _auditWriter.Received(1).AppendAsync(
            tenantId,
            subject.Id,
            subject.Email,
            "FormAccessUpdated",
            RoleNames.User,
            admin.Id,
            admin.Email,
            Arg.Is<string>(d => d.Contains("Form access updated", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await _auditWriter.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<UserId>(),
            Arg.Any<string>(),
            "RoleAssigned",
            Arg.Any<string>(),
            Arg.Any<UserId>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static User CreateUser(string name, string email, RoleId roleId)
    {
        return new User(
            new UserId(Guid.NewGuid()),
            roleId,
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null);
    }
}
