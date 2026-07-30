using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Api.Client;

/// <summary>
/// Ensures role DTOs deserialize camelCase JSON from the API even when
/// Contracts attributes are missing from a published package build.
/// </summary>
public partial class RolesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.PropertyNameCaseInsensitive = true;
    }
}
