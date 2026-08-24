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
        public bool RequestTokenExchange { get; set; } = true;
        
        /// <summary>
        /// Enables automatic user registration when a user authenticates with external IDP for the first time.
        /// When true, users who don't exist in the system will be automatically registered during token exchange.
        /// </summary>
        public bool AutoRegisterUsers { get; set; } = true;
        
        /// <summary>
        /// Optional default template for auto-registration when more than one form is live.
        /// Passed to register when set; the API also reads SelfRegistration:DefaultTemplateId
        /// from tenant settings. Leave unset so new users get no form access until an admin assigns it
        /// (unless the tenant has exactly one live template).
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
