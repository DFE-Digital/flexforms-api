using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Api.Client;

/// <summary>
/// Ensures tenant-admin DTOs deserialize camelCase JSON from the API
/// (same pattern as <see cref="RolesClient"/>).
/// </summary>
public partial class TenantAdminClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.PropertyNameCaseInsensitive = true;
    }
}
