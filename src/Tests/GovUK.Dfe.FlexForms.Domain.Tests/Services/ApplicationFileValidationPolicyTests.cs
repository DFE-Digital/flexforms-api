using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class ApplicationFileValidationPolicyTests
{
    private readonly ApplicationFileValidationPolicy _policy = new();

    [Fact]
    public void Evaluate_Off_Allows_Failed_Files()
    {
        var file = CreateFile();
        file.RequireExternalValidation();
        file.RecordValidationResult(false, "bad", DateTime.UtcNow, null);

        var result = _policy.Evaluate(FileValidationMode.Off, [file]);

        Assert.True(result.CanSubmit);
    }

    [Fact]
    public void Evaluate_FailOnInvalid_Allows_Pending()
    {
        var file = CreateFile();
        file.RequireExternalValidation();

        var result = _policy.Evaluate(FileValidationMode.FailOnInvalid, [file]);

        Assert.True(result.CanSubmit);
    }

    [Fact]
    public void Evaluate_FailOnInvalid_Blocks_Failed()
    {
        var file = CreateFile();
        file.RequireExternalValidation();
        file.RecordValidationResult(false, "bad", DateTime.UtcNow, null);

        var result = _policy.Evaluate(FileValidationMode.FailOnInvalid, [file]);

        Assert.False(result.CanSubmit);
        Assert.Single(result.BlockingFiles);
        Assert.Contains("failed validation", result.ToErrorMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_RequirePassed_Blocks_Pending()
    {
        var file = CreateFile();
        file.RequireExternalValidation();

        var result = _policy.Evaluate(FileValidationMode.RequirePassed, [file]);

        Assert.False(result.CanSubmit);
        Assert.Contains("validated", result.ToErrorMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_RequirePassed_Allows_Passed()
    {
        var file = CreateFile();
        file.RequireExternalValidation();
        file.RecordValidationResult(true, null, DateTime.UtcNow, null);

        var result = _policy.Evaluate(FileValidationMode.RequirePassed, [file]);

        Assert.True(result.CanSubmit);
    }

    private static File CreateFile() =>
        new(
            new FileId(Guid.NewGuid()),
            new Domain.ValueObjects.ApplicationId(Guid.NewGuid()),
            "budget",
            null,
            "budget.xlsx",
            "hashed.xlsx",
            "APP-1",
            DateTime.UtcNow,
            new UserId(Guid.NewGuid()),
            10);
}
