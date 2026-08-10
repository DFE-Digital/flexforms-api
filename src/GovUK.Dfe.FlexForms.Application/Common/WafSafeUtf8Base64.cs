using System.Text;

namespace GovUK.Dfe.FlexForms.Application.Common;

/// <summary>
/// UTF-8 ↔ Base64 helpers for request fields that must stay opaque to Front Door / WAF
/// (same transport pattern as tenant SettingsJson and template schemas).
/// </summary>
public static class WafSafeUtf8Base64
{
    public static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    public static bool TryDecode(string? encoded, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(encoded))
        {
            error = "Value is required.";
            return false;
        }

        try
        {
            value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            error = "Value must be a valid Base64-encoded UTF-8 string.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Decoded value is empty.";
            return false;
        }

        return true;
    }

    public static bool IsValidBase64(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return false;

        try
        {
            Convert.FromBase64String(encoded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
