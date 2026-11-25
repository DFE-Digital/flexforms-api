using System;
using System.Threading;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Provides context about the current authentication mode.
/// Uses AsyncLocal to flow across async operations without requiring method parameters.
/// </summary>
public static class AuthenticationContext
{
    private static readonly AsyncLocal<bool> _useServiceToServiceAuth = new();

    /// <summary>
    /// Gets or sets whether to use service-to-service authentication.
    /// Set to true to force service-to-service authentication (for background consumers).
    /// Set to false or leave unset for normal OBO token authentication (for web requests).
    /// Default: false (OBO token authentication)
    /// </summary>
    public static bool UseServiceToServiceAuth
    {
        get => _useServiceToServiceAuth.Value;
        set => _useServiceToServiceAuth.Value = value;
    }

    /// <summary>
    /// Creates a scope that forces service-to-service authentication.
    /// Use with 'using' statement to ensure cleanup.
    /// </summary>
    /// <example>
    /// using (AuthenticationContext.UseServiceToServiceAuthScope())
    /// {
    ///     // All API calls here will use service-to-service authentication
    ///     await applicationsClient.GetApplicationByReferenceAsync(reference);
    /// }
    /// </example>
    public static IDisposable UseServiceToServiceAuthScope()
    {
        return new ServiceAuthScope();
    }

    private sealed class ServiceAuthScope : IDisposable
    {
        private readonly bool _previousValue;

        public ServiceAuthScope()
        {
            _previousValue = UseServiceToServiceAuth;
            UseServiceToServiceAuth = true;
        }

        public void Dispose()
        {
            UseServiceToServiceAuth = _previousValue;
        }
    }
}

