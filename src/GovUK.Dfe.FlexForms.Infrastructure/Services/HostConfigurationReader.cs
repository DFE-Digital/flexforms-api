using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Builds a flattened, Web-safe host configuration dictionary from root <c>appsettings</c>.
/// Redis remains on the allow-list because the shared web process still binds
/// <c>ConnectionStrings:Redis</c> from this payload at startup. Do not add other secrets.
/// </summary>
public sealed class HostConfigurationReader(IConfiguration configuration) : IHostConfigurationReader
{
    // TM-08: web bootstrap still reads ConnectionStrings:Redis from host-config.
    // Remove Redis from this list only after web has its own Key Vault / managed-identity source.
    private static readonly HashSet<string> AllowedConnectionStringKeys =
        new(StringComparer.OrdinalIgnoreCase) { "Redis" };

    public HostConfigurationSnapshot GetConfiguration(string target)
    {
        var configurationValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var globalSection = configuration.GetSection("GlobalConfiguration");
        if (globalSection.Exists())
        {
            FlattenSection(globalSection, globalSection.Path + ":", configurationValues);
        }

        var loggingSection = configuration.GetSection("Logging");
        if (loggingSection.Exists())
        {
            FlattenSection(loggingSection, loggingSection.Path + ":", configurationValues);
        }

        foreach (var child in configuration.GetSection("ConnectionStrings").GetChildren())
        {
            if (!AllowedConnectionStringKeys.Contains(child.Key) || child.Value is null)
            {
                continue;
            }

            configurationValues[$"ConnectionStrings:{child.Key}"] = child.Value;
        }

        return new HostConfigurationSnapshot(target, DateTime.UtcNow, configurationValues);
    }

    private static void FlattenSection(
        IConfigurationSection section,
        string prefixToRemove,
        Dictionary<string, string?> result)
    {
        foreach (var child in section.GetChildren())
        {
            if (child.Value is not null)
            {
                var trimmedKey = child.Path.StartsWith(prefixToRemove, StringComparison.OrdinalIgnoreCase)
                    ? child.Path[prefixToRemove.Length..]
                    : child.Path;
                if (!IsSecretKey(trimmedKey))
                {
                    result[trimmedKey] = child.Value;
                }
            }

            FlattenSection(child, prefixToRemove, result);
        }
    }

    internal static bool IsSecretKey(string key)
    {
        return key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
               || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
               || key.Contains("Password", StringComparison.OrdinalIgnoreCase)
               || key.Contains("AccessKey", StringComparison.OrdinalIgnoreCase);
    }
}
