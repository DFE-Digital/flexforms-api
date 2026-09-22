using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Redacts secret-bearing JSON leaves for admin APIs, restores sentinels on save,
/// and resolves a single leaf for SuperAdmin reveal.
/// </summary>
public static class TenantSettingSecretJson
{
    public const string Sentinel = TenantSettingSecretRedaction.Sentinel;

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true
    };

    private static readonly string[] SecretNameTokens =
    [
        "Secret",
        "Password",
        "ConnectionString",
        "ApiKey",
        "KeyHash",
        "SigningKey",
        "AccountKey",
        "AccessKey",
        "SasToken",
        "PrivateKey",
        "InstrumentationKey",
        "SharedAccess",
        "ClientSecret",
        "Thumbprint",
        "Certificate"
    ];

    public static TenantSettingDto ToAdminDto(TenantSettingRow row, bool includePlaintextSecrets = false)
    {
        if (!row.IsSecret || includePlaintextSecrets)
        {
            return new TenantSettingDto(
                row.SettingId,
                row.Category,
                row.Target,
                row.SettingsJson,
                row.IsSecret,
                row.UpdatedAtUtc);
        }

        var redacted = Redact(row.SettingsJson, row.Category);
        return new TenantSettingDto(
            row.SettingId,
            row.Category,
            row.Target,
            redacted.Json,
            row.IsSecret,
            row.UpdatedAtUtc,
            redacted.Values);
    }

    public static SecretJsonRedaction Redact(string json, string category)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return new SecretJsonRedaction(Sentinel, [
                new TenantSettingRedactedValueDto("(root)", json.Length, Fingerprint(json))
            ]);
        }

        if (node is null)
        {
            return new SecretJsonRedaction("null", []);
        }

        var values = new List<TenantSettingRedactedValueDto>();
        RedactNode(node, parent: null, slot: null, path: string.Empty, propertyName: null, category, values);
        return new SecretJsonRedaction(node.ToJsonString(IndentedJson), values);
    }

    public static SecretJsonRestore Restore(string proposedJson, string storedJson)
    {
        JsonNode? proposed;
        JsonNode? stored;
        try
        {
            proposed = JsonNode.Parse(proposedJson);
            stored = JsonNode.Parse(storedJson);
        }
        catch (JsonException ex)
        {
            return new SecretJsonRestore(proposedJson, [$"Invalid JSON while restoring redacted secrets: {ex.Message}"]);
        }

        var errors = new List<string>();
        if (proposed is null)
        {
            return new SecretJsonRestore("null", errors);
        }

        if (proposed is JsonValue proposedValue
            && proposedValue.TryGetValue<string>(out var proposedLeaf)
            && IsSentinel(proposedLeaf))
        {
            if (stored is JsonValue storedValue && storedValue.TryGetValue<string>(out var original))
            {
                return new SecretJsonRestore(JsonSerializer.Serialize(original), errors);
            }

            errors.Add("Cannot preserve redacted value at '(root)' because it is not present on the stored setting.");
            return new SecretJsonRestore(proposed.ToJsonString(IndentedJson), errors);
        }

        RestoreNode(proposed, stored, path: string.Empty, errors);
        return new SecretJsonRestore(proposed.ToJsonString(IndentedJson), errors);
    }

    public static bool TryGetValueAtPath(string json, string path, out string value)
    {
        value = string.Empty;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path) || path == "(root)")
        {
            return TryReadString(node, out value);
        }

        var current = node;
        foreach (var segment in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryDescend(ref current, segment))
            {
                return false;
            }
        }

        return current is not null && TryReadString(current, out value);
    }

    public static string Fingerprint(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(hash)[..8].ToLowerInvariant()}";
    }

    public static bool IsSentinel(string? value) =>
        string.Equals(value, Sentinel, StringComparison.Ordinal);

    private static void RedactNode(
        JsonNode node,
        JsonNode? parent,
        object? slot,
        string path,
        string? propertyName,
        string category,
        List<TenantSettingRedactedValueDto> values)
    {
        switch (node)
        {
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var leaf):
                if (!IsSentinel(leaf) && ShouldRedactLeaf(category, propertyName))
                {
                    values.Add(new TenantSettingRedactedValueDto(
                        string.IsNullOrEmpty(path) ? "(root)" : path,
                        leaf.Length,
                        Fingerprint(leaf)));
                    WriteReplacement(parent, slot, Sentinel);
                }
                break;

            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (property.Value is null)
                    {
                        continue;
                    }

                    var childPath = string.IsNullOrEmpty(path)
                        ? property.Key
                        : $"{path}:{property.Key}";
                    RedactNode(property.Value, obj, property.Key, childPath, property.Key, category, values);
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    if (child is null)
                    {
                        continue;
                    }

                    var childPath = $"{path}[{i}]";
                    RedactNode(child, array, i, childPath, propertyName, category, values);
                }
                break;
        }
    }

    private static void RestoreNode(JsonNode proposed, JsonNode? stored, string path, List<string> errors)
    {
        switch (proposed)
        {
            case JsonObject obj:
                RestoreObject(obj, stored as JsonObject, path, errors);
                break;

            case JsonArray array:
                RestoreArray(array, stored as JsonArray, path, errors);
                break;
        }
    }

    private static void RestoreObject(JsonObject proposed, JsonObject? stored, string path, List<string> errors)
    {
        foreach (var property in proposed.ToList())
        {
            var childPath = string.IsNullOrEmpty(path) ? property.Key : $"{path}:{property.Key}";
            var child = property.Value;
            if (child is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var leaf) && IsSentinel(leaf))
            {
                if (stored is not null
                    && stored[property.Key] is JsonValue storedValue
                    && storedValue.TryGetValue<string>(out var original))
                {
                    proposed[property.Key] = original;
                }
                else
                {
                    errors.Add(
                        $"Cannot preserve redacted value at '{childPath}' because it is not present on the stored setting.");
                }
                continue;
            }

            if (child is JsonObject childObject)
            {
                RestoreObject(childObject, stored?[property.Key] as JsonObject, childPath, errors);
            }
            else if (child is JsonArray childArray)
            {
                RestoreArray(childArray, stored?[property.Key] as JsonArray, childPath, errors);
            }
        }
    }

    private static void RestoreArray(JsonArray proposed, JsonArray? stored, string path, List<string> errors)
    {
        for (var i = 0; i < proposed.Count; i++)
        {
            var childPath = $"{path}[{i}]";
            var child = proposed[i];
            if (child is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var leaf) && IsSentinel(leaf))
            {
                if (stored is not null
                    && i < stored.Count
                    && stored[i] is JsonValue storedValue
                    && storedValue.TryGetValue<string>(out var original))
                {
                    proposed[i] = original;
                }
                else
                {
                    errors.Add(
                        $"Cannot preserve redacted value at '{childPath}' because it is not present on the stored setting.");
                }
                continue;
            }

            if (child is JsonObject childObject)
            {
                RestoreObject(childObject, stored is not null && i < stored.Count ? stored[i] as JsonObject : null, childPath, errors);
            }
            else if (child is JsonArray childArray)
            {
                RestoreArray(childArray, stored is not null && i < stored.Count ? stored[i] as JsonArray : null, childPath, errors);
            }
        }
    }

    private static void WriteReplacement(JsonNode? parent, object? slot, string replacement)
    {
        switch (parent)
        {
            case JsonObject obj when slot is string key:
                obj[key] = replacement;
                break;
            case JsonArray array when slot is int index:
                array[index] = replacement;
                break;
        }
    }

    private static bool ShouldRedactLeaf(string category, string? propertyName)
    {
        if (string.Equals(category, "ConnectionStrings", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsSecretPropertyName(propertyName);
    }

    private static bool IsSecretPropertyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.Equals("Key", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Token", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var token in SecretNameTokens)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDescend(ref JsonNode? current, string segment)
    {
        if (current is null)
        {
            return false;
        }

        var match = Regex.Match(segment, @"^(?<name>[^\[\]]+)(?:\[(?<index>\d+)\])*$");
        if (!match.Success)
        {
            var indexOnly = Regex.Match(segment, @"^\[(?<index>\d+)\]$");
            if (!indexOnly.Success || current is not JsonArray rootArray)
            {
                return false;
            }

            var rootIndex = int.Parse(indexOnly.Groups["index"].Value);
            if (rootIndex < 0 || rootIndex >= rootArray.Count)
            {
                return false;
            }

            current = rootArray[rootIndex];
            return current is not null;
        }

        var name = match.Groups["name"].Value;
        if (current is not JsonObject obj || obj[name] is not { } named)
        {
            return false;
        }

        current = named;
        foreach (Capture capture in match.Groups["index"].Captures)
        {
            if (current is not JsonArray array)
            {
                return false;
            }

            var index = int.Parse(capture.Value);
            if (index < 0 || index >= array.Count)
            {
                return false;
            }

            current = array[index];
        }

        return current is not null;
    }

    private static bool TryReadString(JsonNode node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var leaf))
        {
            value = leaf;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

public sealed record SecretJsonRedaction(
    string Json,
    IReadOnlyList<TenantSettingRedactedValueDto> Values);

public sealed record SecretJsonRestore(
    string Json,
    IReadOnlyList<string> Errors);
