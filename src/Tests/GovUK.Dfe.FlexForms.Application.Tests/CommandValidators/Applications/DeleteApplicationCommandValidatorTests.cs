using GovUK.Dfe.FlexForms.Application.Applications.Commands;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandValidators.Applications;

public class DeleteApplicationCommandValidatorTests
{
    [Theory]
    [InlineData("12345678-1234-1234-1234-123456789abc")]
    [InlineData("87654321-4321-4321-4321-ba9876543210")]
    public void Validate_ShouldSucceed_WhenApplicationIdValid(string applicationIdString)
    {
        // Arrange
        var applicationId = Guid.Parse(applicationIdString);
        var command = new DeleteApplicationCommand(applicationId);
        var validator = new DeleteApplicationCommandValidator();

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ShouldFail_WhenApplicationIdEmpty()
    {
        // Arrange
        var command = new DeleteApplicationCommand(Guid.Empty);
        var validator = new DeleteApplicationCommandValidator();

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("Application ID is required", result.Errors[0].ErrorMessage);
        Assert.Equal("ApplicationId", result.Errors[0].PropertyName);
    }
} 