using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Templates.Commands;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using MockQueryable;
using MockQueryable.NSubstitute;
using NSubstitute;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Templates;

public class UpdateCustomApplicationStatusCommandHandlerTests
{
    private static ITenantTemplateResolver AllowAllTenantTemplates()
    {
        var resolver = Substitute.For<ITenantTemplateResolver>();
        resolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return resolver;
    }

    private readonly IEaRepository<CustomApplicationStatus> _customStatusRepo = Substitute.For<IEaRepository<CustomApplicationStatus>>();
    private readonly IEaRepository<User> _userRepo = Substitute.For<IEaRepository<User>>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly UpdateCustomApplicationStatusCommandHandler _handler;

    private readonly User _testUser;
    private readonly Guid _testTemplateId = Guid.NewGuid();
    private readonly string _testUserEmail = "test@example.com";

    public UpdateCustomApplicationStatusCommandHandlerTests()
    {
        _testUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            _testUserEmail,
            DateTime.UtcNow,
            null,
            null,
            null);

        // Set Role property for Include() support
        var role = new Role(_testUser.RoleId, "Test Role");
        typeof(User).GetProperty("Role")!.SetValue(_testUser, role);

        var userQueryable = new[] { _testUser }.AsQueryable().BuildMock();
        _userRepo.Query().Returns(userQueryable);

        var claims = new List<Claim> { new(ClaimTypes.Email, _testUserEmail) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = claimsPrincipal };
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _handler = new UpdateCustomApplicationStatusCommandHandler(
            _customStatusRepo,
            _userRepo,
            _httpContextAccessor,
            AllowAllTenantTemplates(),
            _unitOfWork);
    }
    [Theory]
    [CustomAutoData]
    public async Task Handle_UpdatesLabel_WhenCustomStatusExists(string newLabel)
    {
        // Arrange
        var originalCreatedOn = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var originalCreatedBy = new UserId(Guid.NewGuid());

        var existingStatus = new CustomApplicationStatus(
            new CustomApplicationStatusId(Guid.NewGuid()),
            new TemplateId(_testTemplateId),
            ApplicationStatus.Submitted,
            "Old Label",
            originalCreatedOn,
            originalCreatedBy
        );

        var statusQueryable = new List<CustomApplicationStatus> { existingStatus }.AsQueryable().BuildMock();
        _customStatusRepo.Query().Returns(_ => statusQueryable);

        var command = new UpdateCustomApplicationStatusCommand(_testTemplateId, ApplicationStatus.Submitted, newLabel);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.Equal(newLabel, result.Value.Label);
        Assert.Equal(ApplicationStatus.Submitted, result.Value.ApplicationStatus);
        Assert.Equal(originalCreatedOn, result.Value.CreatedOn);
        Assert.Equal(originalCreatedBy.Value, result.Value.CreatedBy);
        await _unitOfWork.Received(1).CommitAsync(CancellationToken.None);
        await _customStatusRepo.DidNotReceive().AddAsync(Arg.Any<CustomApplicationStatus>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ReturnsForbid_WhenUserNotAuthenticated(string label)
    {
        // Arrange
        var emptyStatusList = new List<CustomApplicationStatus>().AsQueryable().BuildMock();
        _customStatusRepo.Query().Returns(emptyStatusList);

        // Unauthenticated user
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        httpContextAccessor.HttpContext.Returns(httpContext);

        var handler = new UpdateCustomApplicationStatusCommandHandler(
            _customStatusRepo,
            _userRepo,
            httpContextAccessor,
            AllowAllTenantTemplates(),
            _unitOfWork);

        var command = new UpdateCustomApplicationStatusCommand(_testTemplateId, ApplicationStatus.Submitted, label);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);
        await _customStatusRepo.DidNotReceive().AddAsync(Arg.Any<CustomApplicationStatus>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(CancellationToken.None);
    }

    [Theory]
    [CustomAutoData]
    public async Task Handle_ReturnsForbid_WhenUserNotFoundInDatabase(string label)
    {
        // Arrange
        var emptyStatusList = new List<CustomApplicationStatus>().AsQueryable().BuildMock();
        _customStatusRepo.Query().Returns(emptyStatusList);

        // User not found in database
        var emptyUserList = new List<User>().AsQueryable().BuildMock();
        var userRepo = Substitute.For<IEaRepository<User>>();
        userRepo.Query().Returns(emptyUserList);

        var handler = new UpdateCustomApplicationStatusCommandHandler(
            _customStatusRepo,
            userRepo,
            _httpContextAccessor,
            AllowAllTenantTemplates(),
            _unitOfWork);

        var command = new UpdateCustomApplicationStatusCommand(_testTemplateId, ApplicationStatus.Submitted, label);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Unable to resolve CreatedBy user", result.Error);
        await _customStatusRepo.DidNotReceive().AddAsync(Arg.Any<CustomApplicationStatus>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(CancellationToken.None);
    }
}
