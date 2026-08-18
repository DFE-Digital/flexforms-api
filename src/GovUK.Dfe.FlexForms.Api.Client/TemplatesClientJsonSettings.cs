using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Api.Client;

/// <summary>
/// Ensures template DTOs (including grant-to-all-users counts) deserialize camelCase JSON from the API.
/// </summary>
public partial class TemplatesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.PropertyNameCaseInsensitive = true;
    }
}
