using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Central service for managing all token states and transitions
/// Single responsibility: Token state management across all authentication schemes
/// </summary>
public interface ITokenStateManager
{
    /// <summary>
    /// Gets the current comprehensive token state for the authenticated user
    /// </summary>
    Task<TokenState> GetCurrentTokenStateAsync();

    /// <summary>
    /// Forces complete logout and clears all tokens and caches atomically
    /// </summary>
    Task<bool> ForceCompleteLogoutAsync();

    /// <summary>
    /// Attempts to refresh tokens if possible based on authentication scheme
    /// </summary>
    Task<bool> RefreshTokensIfPossibleAsync();

    /// <summary>
    /// Determines if user should be forced to logout based on token state
    /// </summary>
    bool ShouldForceLogout(TokenState state);

    /// <summary>
    /// Checks if logout has been flagged for the current user
    /// </summary>
    bool IsLogoutRequired();
}

/// <summary>
/// Comprehensive token state containing all token information and computed properties
/// </summary>
public class TokenState
{
    public bool IsAuthenticated { get; set; }
    public string? AuthenticationScheme { get; set; }
    public string? UserId { get; set; }
    public TokenInfo ExternalIdpToken { get; set; } = new();
    public TokenInfo OboToken { get; set; } = new();
    public TokenInfo AzureToken { get; set; } = new();
    public DateTime CheckTime { get; set; } = DateTime.UtcNow;

    // Computed properties
    public bool IsAnyTokenExpired => ExternalIdpToken.IsExpired || OboToken.IsExpired;
    public bool ShouldLogout => IsAnyTokenExpired && !CanRefresh;
    public bool CanRefresh { get; set; }
    public DateTime? EarliestExpiry => new[] { ExternalIdpToken.ExpiryTime, OboToken.ExpiryTime }
        .Where(x => x.HasValue)
        .Min();
    public string? LogoutReason { get; set; }
}

/// <summary>
/// Information about an individual token
/// </summary>
public class TokenInfo
{
    public string? Value { get; set; }
    public DateTime? ExpiryTime { get; set; }
    public bool IsPresent => !string.IsNullOrEmpty(Value);
    public bool IsExpired => ExpiryTime.HasValue && (ExpiryTime.Value - DateTime.UtcNow) <= TimeSpan.FromMinutes(5);
    public bool IsValid => IsPresent && !IsExpired;
    public TimeSpan? TimeUntilExpiry => ExpiryTime?.Subtract(DateTime.UtcNow);
}
