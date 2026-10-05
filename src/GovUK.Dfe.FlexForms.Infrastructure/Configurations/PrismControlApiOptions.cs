namespace GovUK.Dfe.FlexForms.Infrastructure.Configurations;

/// <summary>Bound from <c>Prism:ControlApi</c>. Without a base URL the reporting export endpoints report that it isn't set up.</summary>
public sealed class PrismControlApiOptions
{
    public const string SectionName = "Prism:ControlApi";

    /// <summary>The Prism Function App, for example <c>https://prism-func.azurewebsites.net/</c> or <c>http://localhost:7071/</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The Prism app registration's scope, for example <c>api://&lt;prism-client-id&gt;/.default</c>.</summary>
    public string? Scope { get; set; }

    /// <summary>The user-assigned managed identity to get tokens with; unset uses the default Azure credential chain.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Local development only: sent as <c>X-Prism-Development-Key</c> instead of a token. Prism only accepts it when
    /// its own environment is Development.
    /// </summary>
    public string? DevelopmentKey { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
