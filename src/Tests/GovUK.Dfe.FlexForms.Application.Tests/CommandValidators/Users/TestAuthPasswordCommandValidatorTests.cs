using FluentValidation.TestHelper;
using GovUK.Dfe.FlexForms.Application.Users.Commands;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandValidators.Users;

public class SendTestAuthPasswordCommandValidatorTests
{
    private readonly SendTestAuthPasswordCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldSucceed_WhenEmailValid()
    {
        var result = _validator.TestValidate(new SendTestAuthPasswordCommand("tester@education.gov.uk"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-an-email")]
    public void Validate_ShouldFail_WhenEmailMissingOrInvalid(string? email)
    {
        var result = _validator.TestValidate(new SendTestAuthPasswordCommand(email!));

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }
}

public class VerifyTestAuthPasswordCommandValidatorTests
{
    private readonly VerifyTestAuthPasswordCommandValidator _validator = new();

    [Theory]
    [InlineData("123456")]
    [InlineData("000000")]
    [InlineData(" 123456 ")]
    public void Validate_ShouldSucceed_WhenEmailAndSixDigitPasswordProvided(string password)
    {
        var result = _validator.TestValidate(new VerifyTestAuthPasswordCommand("tester@education.gov.uk", password));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("12 456")]
    public void Validate_ShouldFail_WhenPasswordMissingOrNotSixDigits(string? password)
    {
        var result = _validator.TestValidate(new VerifyTestAuthPasswordCommand("tester@education.gov.uk", password!));

        result.ShouldHaveValidationErrorFor(c => c.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_ShouldFail_WhenEmailMissingOrInvalid(string email)
    {
        var result = _validator.TestValidate(new VerifyTestAuthPasswordCommand(email, "123456"));

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }
}
