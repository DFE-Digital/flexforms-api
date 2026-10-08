using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Api.Client;

/// <summary>
/// Ensures reporting-export DTOs deserialize camelCase JSON from the API
/// (same pattern as <see cref="RolesClient"/>).
/// </summary>
public partial class ReportingExportClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.PropertyNameCaseInsensitive = true;
    }
}
