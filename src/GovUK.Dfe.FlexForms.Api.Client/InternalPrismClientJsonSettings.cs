using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Api.Client;

/// <summary>
/// Ensures Prism DTOs (positional records) deserialize camelCase JSON from the API;
/// without this every id binds as Guid.Empty.
/// </summary>
public partial class InternalPrismClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.PropertyNameCaseInsensitive = true;
    }
}
