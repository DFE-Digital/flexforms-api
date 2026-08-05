using FluentValidation;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

internal class UpsertTenantSettingCommandValidator : AbstractValidator<UpsertTenantSettingCommand>
{
    private static readonly string[] ValidTargets = ["Shared", "Api", "Web"];

    public UpsertTenantSettingCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty()
            .WithMessage("Tenant ID is required.");

        RuleFor(x => x.Category)
            .NotEmpty()
            .WithMessage("Category is required.")
            .MaximumLength(50)
            .WithMessage("Category must not exceed 50 characters.");

        RuleFor(x => x.Target)
            .NotEmpty()
            .WithMessage("Target is required.")
            .Must(t => ValidTargets.Contains(t))
            .WithMessage("Target must be one of: Shared, Api, Web.");

        RuleFor(x => x.SettingsJson)
            .NotEmpty()
            .WithMessage("Settings JSON is required.")
            .Must(BeValidBase64)
            .WithMessage("SettingsJson must be a valid Base64 encoded string.");
    }

    private static bool BeValidBase64(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return false;

        try
        {
            Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
