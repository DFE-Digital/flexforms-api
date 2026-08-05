using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;

/// <summary>
/// How strictly tenant setting JSON is validated.
/// </summary>
public enum TenantSettingValidationMode
{
    /// <summary>
    /// Accept any parseable JSON. Used for promotion import so export→import round-trips
    /// never fail on seeded shapes (arrays, scalar feature flags, string booleans, etc.).
    /// </summary>
    Lenient = 0,

    /// <summary>
    /// Apply structural checks for known auth/connection categories. Used for UI upserts.
    /// </summary>
    Strict = 1
}

/// <summary>
/// Validates decoded TenantConfig JSON per known category before persistence.
/// </summary>
public static class TenantSettingJsonValidator
{
    private const string SecretPlaceholder = "__SECRET__";

    public static IReadOnlyList<string> Validate(
        string category,
        string target,
        string settingsJson,
        TenantSettingValidationMode mode = TenantSettingValidationMode.Strict)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            errors.Add("Settings JSON is required.");
            return errors;
        }

        if (IsSecretPlaceholder(settingsJson))
        {
            return errors;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(settingsJson);
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
            return errors;
        }

        using (doc)
        {
            // Lenient (import): any parseable JSON is enough for round-trip safety.
            if (mode == TenantSettingValidationMode.Lenient)
            {
                return errors;
            }

            var root = doc.RootElement;
            if (RequiresObjectRoot(category) && root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Settings JSON must be a JSON object.");
                return errors;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                ValidateCategory(category, target, root, errors);
            }
        }

        return errors;
    }

    public static bool IsSecretPlaceholder(string settingsJson)
    {
        if (string.Equals(settingsJson.Trim(), SecretPlaceholder, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("__SECRET__", out var flag)
                   && flag.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool RequiresObjectRoot(string category) =>
        category.Trim() switch
        {
            "Authentication" => true,
            "TestAuthentication" => true,
            "EntraSso" => true,
            "DfESignIn" => true,
            "Authorization" => true,
            "ConnectionStrings" => true,
            "InternalServiceAuth" => true,
            _ => false
        };

    private static void ValidateCategory(
        string category,
        string target,
        JsonElement root,
        List<string> errors)
    {
        switch (category.Trim())
        {
            case "Authentication":
                // Scheme is recommended but may be omitted when relying on provider flags.
                if (root.TryGetProperty("Scheme", out _) && GetString(root, "Scheme") is null)
                {
                    errors.Add("Scheme must be a non-empty string when present.");
                }
                break;

            case "TestAuthentication":
                if (root.TryGetProperty("Enabled", out _) && GetBool(root, "Enabled") is null)
                {
                    errors.Add("Enabled must be true or false.");
                }
                else if (GetBool(root, "Enabled") == true)
                {
                    RequireString(root, "JwtSigningKey", errors);
                    RequireString(root, "JwtIssuer", errors);
                    RequireString(root, "JwtAudience", errors);
                }
                break;

            case "EntraSso":
                if (root.TryGetProperty("Enabled", out _) && GetBool(root, "Enabled") is null)
                {
                    errors.Add("Enabled must be true or false.");
                }
                else if (GetBool(root, "Enabled") == true)
                {
                    RequireString(root, "TenantId", errors);
                    RequireString(root, "ClientId", errors);
                }
                break;

            case "DfESignIn":
                // Incomplete stubs are common in seeded tenants; only validate when keys exist.
                if (root.TryGetProperty("Authority", out _) && GetString(root, "Authority") is null)
                {
                    errors.Add("Authority must be a non-empty string when present.");
                }
                if (root.TryGetProperty("ClientId", out _) && GetString(root, "ClientId") is null)
                {
                    errors.Add("ClientId must be a non-empty string when present.");
                }
                break;

            case "Authorization":
                if (string.Equals(target, "Api", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(target, "Shared", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("TokenSettings", out var tokenSettings)
                        && tokenSettings.ValueKind == JsonValueKind.Object)
                    {
                        RequireStringIfPresent(tokenSettings, "SecretKey", errors, "TokenSettings.");
                        RequireStringIfPresent(tokenSettings, "Issuer", errors, "TokenSettings.");
                        RequireStringIfPresent(tokenSettings, "Audience", errors, "TokenSettings.");
                    }
                    else if (HasAnyProperty(root, "SecretKey", "Issuer", "Audience"))
                    {
                        RequireStringIfPresent(root, "SecretKey", errors);
                        RequireStringIfPresent(root, "Issuer", errors);
                        RequireStringIfPresent(root, "Audience", errors);
                    }
                }
                break;

            case "ConnectionStrings":
                // Named connections vary by tenant; require at least one non-empty string value.
                if (!HasAnyNonEmptyStringValue(root))
                {
                    errors.Add("At least one connection string value is required.");
                }
                break;

            case "InternalServiceAuth":
                if (HasAnyProperty(root, "SecretKey", "Issuer", "Audience"))
                {
                    RequireStringIfPresent(root, "SecretKey", errors);
                    RequireStringIfPresent(root, "Issuer", errors);
                    RequireStringIfPresent(root, "Audience", errors);
                }
                break;

            default:
                // Unknown categories: any JSON object/array/scalar is accepted.
                break;
        }
    }

    private static void RequireString(
        JsonElement root,
        string property,
        List<string> errors,
        string pathPrefix = "")
    {
        if (GetString(root, property) is null)
        {
            errors.Add($"{pathPrefix}{property} is required.");
        }
    }

    private static void RequireStringIfPresent(
        JsonElement root,
        string property,
        List<string> errors,
        string pathPrefix = "")
    {
        if (root.TryGetProperty(property, out _) && GetString(root, property) is null)
        {
            errors.Add($"{pathPrefix}{property} must be a non-empty string when present.");
        }
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(value.GetString()) => value.GetString(),
            // IConfiguration / JSON number coercion
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool? GetBool(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt64(out var n) && n is 0 or 1 => n == 1,
            _ => null
        };
    }

    private static bool HasAnyProperty(JsonElement root, params string[] names)
        => names.Any(name => root.TryGetProperty(name, out _));

    private static bool HasAnyNonEmptyStringValue(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                return true;
            }
        }

        return false;
    }
}
