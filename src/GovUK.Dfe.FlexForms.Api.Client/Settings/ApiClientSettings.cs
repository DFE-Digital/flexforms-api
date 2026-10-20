using System;

namespace GovUK.Dfe.FlexForms.Api.Client.Settings
{
    public class ApiClientSettings
    {
        public string? BaseUrl { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? Authority { get; set; }
        public string? Scope { get; set; }

        /// <summary>
        /// Acquires service tokens with the host's Azure managed identity instead of a client secret.
        /// Only <see cref="Scope"/> is required; <see cref="ClientId"/>, <see cref="ClientSecret"/> and
        /// <see cref="Authority"/> are ignored.
        /// </summary>
        public bool UseManagedIdentity { get; set; }

        /// <summary>
        /// Client ID of a user-assigned managed identity. Leave unset to use the system-assigned identity.
        /// Only used when <see cref="UseManagedIdentity"/> is true.
        /// </summary>
        public string? ManagedIdentityClientId { get; set; }

        public bool RequestTokenExchange { get; set; } = true;
        
        /// <summary>
        /// Enables automatic user registration when a user authenticates with external IDP for the first time.
        /// When true, users who don't exist in the system will be automatically registered during token exchange.
        /// </summary>
        public bool AutoRegisterUsers { get; set; } = true;
        
        /// <summary>
        /// Optional default template for auto-registration when more than one form is live.
        /// Passed to register when set, which bypasses the API's tenant-level defaults
        /// (SelfRegistration:DefaultTemplateIds / DefaultTemplateId). Prefer leaving this unset and
        /// configuring those tenant settings instead.
        /// </summary>
        public Guid? DefaultTemplateId { get; set; }
        
        /// <summary>
        /// List of HTTP headers to forward from incoming requests to API calls.
        /// Useful for forwarding authentication-related headers (e.g., X-Cypress-Test, X-Cypress-Secret).
        /// If null or empty, a default set of common headers will be forwarded.
        /// </summary>
        public string[]? HeadersToForward { get; set; }
        
        /// <summary>
        /// The unique identifier of the tenant this client is associated with.
        /// This ID will be automatically included as the X-Tenant-ID header in all API requests.
        /// Required for multi-tenant API access.
        /// </summary>
        public Guid? TenantId { get; set; }
    }
}
