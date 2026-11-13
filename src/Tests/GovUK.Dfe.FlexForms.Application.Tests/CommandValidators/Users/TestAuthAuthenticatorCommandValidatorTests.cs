using FluentValidation.TestHelper;
using GovUK.Dfe.FlexForms.Application.Users.Commands;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandValidators.Users;

public class VerifyTestAuthAuthenticatorCodeCommandValidatorTests
{
    private readonly VerifyTestAuthAuthenticatorCodeCommandValidator _validator = new();

    [Theory]
    [InlineData("123456")]
    [InlineData(" 123456 ")]
    public void Validate_ShouldSucceed_WhenEmailAndSixDigitCodeProvided(string code)
    {
        var result = _validator.TestValidate(new VerifyTestAuthAuthenticatorCodeCommand("tester@education.gov.uk", code));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Validate_ShouldFail_WhenCodeMissingOrNotSixDigits(string? code)
    {
        var result = _validator.TestValidate(new VerifyTestAuthAuthenticatorCodeCommand("tester@education.gov.uk", code!));

        result.ShouldHaveValidationErrorFor(c => c.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_ShouldFail_WhenEmailMissingOrInvalid(string email)
    {
        var result = _validator.TestValidate(new VerifyTestAuthAuthenticatorCodeCommand(email, "123456"));

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }
}

public class TestAuthAuthenticatorInviteCommandValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Create_ShouldFail_WhenEmailMissingOrInvalid(string email)
    {
        new CreateTestAuthAuthenticatorInviteCommandValidator()
            .TestValidate(new CreateTestAuthAuthenticatorInviteCommand(email))
            .ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Create_ShouldSucceed_WhenEmailValid()
    {
        new CreateTestAuthAuthenticatorInviteCommandValidator()
            .TestValidate(new CreateTestAuthAuthenticatorInviteCommand("tester@education.gov.uk"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDE-FGHJK-ABCDE-FGHJK-ABCDE-FGHJK")]
    public void Redeem_ShouldFail_WhenCodeMissingOrTooLong(string code)
    {
        new RedeemTestAuthAuthenticatorInviteCommandValidator()
            .TestValidate(new RedeemTestAuthAuthenticatorInviteCommand("tester@education.gov.uk", code))
            .ShouldHaveValidationErrorFor(c => c.InviteCode);
    }

    [Fact]
    public void Redeem_ShouldSucceed_WhenEmailAndCodeProvided()
    {
        new RedeemTestAuthAuthenticatorInviteCommandValidator()
            .TestValidate(new RedeemTestAuthAuthenticatorInviteCommand("tester@education.gov.uk", "abcde fghjk"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ConfirmInvite_ShouldFail_WhenCodeNotSixDigits()
    {
        new ConfirmTestAuthAuthenticatorInviteSetupCommandValidator()
            .TestValidate(new ConfirmTestAuthAuthenticatorInviteSetupCommand("tester@education.gov.uk", "12345"))
            .ShouldHaveValidationErrorFor(c => c.Code);
    }

    [Fact]
    public void ConfirmInvite_ShouldFail_WhenEmailInvalid()
    {
        new ConfirmTestAuthAuthenticatorInviteSetupCommandValidator()
            .TestValidate(new ConfirmTestAuthAuthenticatorInviteSetupCommand("not-an-email", "123456"))
            .ShouldHaveValidationErrorFor(c => c.Email);
    }
}

public class ConfirmTestAuthAuthenticatorSetupCommandValidatorTests
{
    private readonly ConfirmTestAuthAuthenticatorSetupCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldSucceed_WhenSixDigitCodeProvided()
    {
        _validator.TestValidate(new ConfirmTestAuthAuthenticatorSetupCommand("123456")).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("abcdef")]
    public void Validate_ShouldFail_WhenCodeMissingOrNotSixDigits(string? code)
    {
        _validator.TestValidate(new ConfirmTestAuthAuthenticatorSetupCommand(code!)).ShouldHaveValidationErrorFor(c => c.Code);
    }
}
