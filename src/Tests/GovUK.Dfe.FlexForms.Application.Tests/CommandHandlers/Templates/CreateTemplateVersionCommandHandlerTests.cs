using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.Commands;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using MockQueryable;
using MockQueryable.NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Templates
{
    public class CreateTemplateVersionCommandHandlerTests
    {
        private readonly IEaRepository<Template> _templateRepo = Substitute.For<IEaRepository<Template>>();
        private readonly IEaRepository<User> _userRepo = Substitute.For<IEaRepository<User>>();
        private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
        private readonly ITenantTemplateResolver _tenantTemplateResolver = Substitute.For<ITenantTemplateResolver>();
        private readonly ITemplateFactory _templateFactory = Substitute.For<ITemplateFactory>();
        private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        private readonly ITemplateSchemaCacheInvalidator _templateSchemaCacheInvalidator =
            Substitute.For<ITemplateSchemaCacheInvalidator>();
        private readonly CreateTemplateVersionCommandHandler _handler;
        
        private readonly User _testUser;
        private readonly Template _testTemplate;
        private readonly string _testJsonSchema = "{ \"some\": \"schema\" }";
        private readonly string _testBase64JsonSchema;

        public CreateTemplateVersionCommandHandlerTests()
        {
            _tenantTemplateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
                .Returns(true);

            _handler = new CreateTemplateVersionCommandHandler(
                _templateRepo,
                _userRepo,
                _httpContextAccessor,
                _permissionChecker,
                _tenantTemplateResolver,
                _templateFactory,
                _unitOfWork,
                _templateSchemaCacheInvalidator);
            
            _testUser = new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "Test", "test@test.com", DateTime.UtcNow, null, null, null);
            
            var templateId = new TemplateId(Guid.NewGuid());
            _testTemplate = new Template(templateId, "Test Template", DateTime.UtcNow, _testUser.Id!);
            
            _testBase64JsonSchema = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_testJsonSchema));

            var userQueryable = new[] { _testUser }.AsQueryable().BuildMock();
            _userRepo.Query().Returns(userQueryable);
            
            var templateQueryable = new[] { _testTemplate }.AsQueryable().BuildMock();
            _templateRepo.Query().Returns(templateQueryable);

            var claims = new List<Claim> { new(ClaimTypes.Email, _testUser.Email) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var claimsPrincipal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext { User = claimsPrincipal };
            _httpContextAccessor.HttpContext.Returns(httpContext);
        }

        [Fact]
        public async Task Handle_ReturnsSuccess_WhenRequestIsValid()
        {
            // Arrange
            var command = new CreateTemplateVersionCommand(_testTemplate.Id!.Value, "1.0.0", _testBase64JsonSchema);
            _permissionChecker.HasTemplatePermission(_testTemplate.Id.Value.ToString(), AccessType.Write).Returns(true);
            var newVersion = new TemplateVersion(new TemplateVersionId(Guid.NewGuid()), _testTemplate.Id, "1.0.0", _testJsonSchema, DateTime.UtcNow, _testUser.Id);
            _templateFactory.AddVersionToTemplate(_testTemplate, command.VersionNumber, _testJsonSchema, _testUser.Id!).Returns(newVersion);
            
            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(newVersion.Id.Value, result.Value.TemplateVersionId);
            await _unitOfWork.Received(1).CommitAsync(CancellationToken.None);
            await _templateSchemaCacheInvalidator.Received(1).InvalidateForTemplateAsync(
                _testTemplate.Id!.Value,
                Arg.Any<CancellationToken>());
        }
        
        [Fact]
        public async Task Handle_ReturnsFailure_WhenUserIsNotAuthenticated()
        {
            // Arrange
            _httpContextAccessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // Not authenticated
            var command = new CreateTemplateVersionCommand(_testTemplate.Id!.Value, "1.0.0", _testBase64JsonSchema);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Not authenticated", result.Error);
        }

        [Fact]
        public async Task Handle_ReturnsFailure_WhenUserNotFound()
        {
            // Arrange
            _userRepo.Query().Returns(new List<User>().AsQueryable().BuildMock());
            var command = new CreateTemplateVersionCommand(_testTemplate.Id!.Value, "1.0.0", _testBase64JsonSchema);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User not found", result.Error);
        }
        
        [Fact]
        public async Task Handle_ReturnsFailure_WhenSchemaIsInvalidBase64()
        {
            // Arrange
            var command = new CreateTemplateVersionCommand(_testTemplate.Id!.Value, "1.0.0", "not-base64");

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid Base64 format for JsonSchema", result.Error);
        }

        [Fact]
        public async Task Handle_ReturnsFailure_WhenTemplateNotFound()
        {
            // Arrange
            var command = new CreateTemplateVersionCommand(Guid.NewGuid(), "1.0.0", _testBase64JsonSchema);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Template not found", result.Error);
        }
        
        [Fact]
        public async Task Handle_ReturnsFailure_WhenPermissionDenied()
        {
            // Arrange
            var command = new CreateTemplateVersionCommand(_testTemplate.Id!.Value, "1.0.0", _testBase64JsonSchema);
            _permissionChecker.HasTemplatePermission(_testTemplate.Id.Value.ToString(), AccessType.Write).Returns(false);
            
            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Access denied", result.Error);
        }
        
        [Fact]
        public async Task Handle_ReturnsFailure_WhenFactoryThrowsException()
        {
            // Arrange
            var command = new CreateTemplateVersionCommand(_testTemplate.Id!.Value, "1.0.0", _testBase64JsonSchema);
            _permissionChecker.HasTemplatePermission(_testTemplate.Id.Value.ToString(), AccessType.Write).Returns(true);
            _templateFactory.When(f => f.AddVersionToTemplate(Arg.Any<Template>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<UserId>()))
                .Do(_ => throw new InvalidOperationException("Version exists"));

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Version exists", result.Error);
        }
    }
} 