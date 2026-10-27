using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Builds promotion bundles (export with redacted secrets) and applies imports.
/// </summary>
public sealed class TenantConfigurationPromotionService(
    ITenantSettingsQuery settingsQuery,
    ITenantSettingsWriter settingsWriter,
    ITenantSettingAuditWriter auditWriter) : ITenantConfigurationPromotion
{
    public const string SecretPlaceholder = "__SECRET__";

    public async Task<ExportTenantConfigurationDto?> ExportAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var list = await settingsQuery.ListSettingsAsync(tenantId, cancellationToken);
        if (list is null)
        {
            return null;
        }

        var settings = list.Settings.Select(row =>
        {
            var json = row.IsSecret
                ? JsonSerializer.Serialize(new { __SECRET__ = true })
                : row.SettingsJson;

            return new TenantSettingExportDto(
                row.Category,
                row.Target,
                json,
                row.IsSecret,
                SecretRedacted: row.IsSecret);
        }).ToList();

        return new ExportTenantConfigurationDto(
            list.TenantId,
            list.TenantName,
            DateTimeOffset.UtcNow,
            settings);
    }

    public async Task<ImportTenantConfigurationResultDto> ImportAsync(
        Guid tenantId,
        ImportTenantConfigurationDto bundle,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var applied = 0;
        var skipped = 0;

        await EnsureNoTargetOverlapsAsync(tenantId, bundle, cancellationToken);

        foreach (var item in bundle.Settings)
        {
            if (bundle.SkipSecretPlaceholders
                && IsSecretPlaceholderJson(item.SettingsJson))
            {
                skipped++;
                messages.Add($"Skipped secret placeholder {item.Category}/{item.Target}.");
                continue;
            }

            var isSecret = item.IsSecret || TenantSettingsSecretCategories.ShouldEncrypt(item.Category);

            var result = await settingsWriter.UpsertSettingAsync(
                tenantId,
                item.Category,
                item.Target,
                item.SettingsJson,
                isSecret,
                cancellationToken);

            await auditWriter.AppendAsync(
                tenantId,
                item.Category,
                item.Target,
                result.WasCreated ? "Created" : "Updated",
                actorEmail,
                isSecret,
                cancellationToken);

            applied++;
        }

        return new ImportTenantConfigurationResultDto(applied, skipped, messages);
    }

    /// <summary>
    /// Checked up front so a rejected bundle is not left half-applied.
    /// </summary>
    private async Task EnsureNoTargetOverlapsAsync(
        Guid tenantId,
        ImportTenantConfigurationDto bundle,
        CancellationToken cancellationToken)
    {
        var existing = await settingsQuery.ListSettingsAsync(tenantId, cancellationToken);

        var resulting = (existing?.Settings.Select(s => (s.Category, s.Target)) ?? [])
            .Concat(bundle.Settings
                .Where(item => !(bundle.SkipSecretPlaceholders && IsSecretPlaceholderJson(item.SettingsJson)))
                .Select(item => (item.Category, item.Target)));

        var overlaps = TenantSettingTargetOverlap.FindOverlaps(resulting);
        if (overlaps.Count > 0)
        {
            throw new TenantSettingTargetConflictException(
                "Import would leave settings under both Shared and an app-specific target: " +
                $"{string.Join(", ", overlaps)}. A category must be either Shared or app-specific (Api/Web), not both.");
        }
    }

    private static bool IsSecretPlaceholderJson(string json)
    {
        if (string.Equals(json.Trim(), SecretPlaceholder, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("__SECRET__", out var flag)
                   && flag.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
