using GovUK.Dfe.FlexForms.Application.Users.Commands;
using FluentValidation.TestHelper;
using Xunit;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandValidators.Users;

public class RegisterUserCommandValidatorTests
{
    private readonly RegisterUserCommandValidator _validator;

    public RegisterUserCommandValidatorTests()
    {
        _validator = new RegisterUserCommandValidator();
    }

    [Fact]
    public void Should_Pass_When_Valid_Request()
    {
        // Arrange
        var command = new RegisterUserCommand("valid-token-string", Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_When_TemplateId_Is_Provided()
    {
        // Arrange
        var command = new RegisterUserCommand("valid-token-string", Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_When_TemplateId_Is_Null()
    {
        // Arrange
        var command = new RegisterUserCommand("valid-token-string", null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Should_Fail_When_SubjectToken_Is_Empty(string? token)
    {
        // Arrange
        var command = new RegisterUserCommand(token!);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.SubjectToken)
            .WithErrorMessage("Subject token is required");
    }

    [Fact]
    public void Should_Pass_When_SubjectToken_Is_Long_Jwt()
    {
        // Arrange
        var longToken = new string('a', 1000); // Simulate a long JWT token
        var command = new RegisterUserCommand(longToken, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_When_SubjectToken_Contains_Special_Characters()
    {
        // Arrange
        var tokenWithSpecialChars = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var command = new RegisterUserCommand(tokenWithSpecialChars, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}

