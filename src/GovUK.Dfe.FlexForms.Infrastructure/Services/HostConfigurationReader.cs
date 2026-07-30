using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Builds a flattened, Web-safe host configuration dictionary from root <c>appsettings</c>.
/// </summary>
public sealed class HostConfigurationReader(IConfiguration configuration) : IHostConfigurationReader
{
    private static readonly HashSet<string> AllowedConnectionStringKeys =
        new(StringComparer.OrdinalIgnoreCase) { "Redis" };

    public HostConfigurationSnapshot GetConfiguration(string target)
    {
        Console.WriteLine($"Hello2");
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
                result[trimmedKey] = child.Value;
            }

            FlattenSection(child, prefixToRemove, result);
        }
    }
}
