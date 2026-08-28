namespace GovUK.Dfe.FlexForms.Application.Common.Models;

/// <summary>
/// Configuration for mapping host-friendly names to form template GUIDs.
/// </summary>
public class ApplicationTemplatesConfiguration
{
    /// <summary>
    /// Maps HostMappings keys (short names or hostnames) to form template GUIDs.
    /// Keys must align (case-insensitive) with EmailTemplates product keys after taking the first DNS label.
    /// </summary>
    public Dictionary<string, string> HostMappings { get; set; } = new();
}
