namespace GovUK.Dfe.FlexForms.Application.Security;

/// <summary>
/// One-time codes an administrator gives a user so they can set up a Test Authentication authenticator app without
/// first receiving an emailed password. Codes are per tenant and per email, single use, and expire after
/// <see cref="TestAuthAuthenticatorInviteService.CodeLifetime"/>.
/// </summary>
public interface ITestAuthAuthenticatorInviteService
{
    /// <summary>Creates a code for <paramref name="email"/>, replacing any unused code for that email.</summary>
    Task<TestAuthAuthenticatorInvite> IssueAsync(string email);

    /// <summary>
    /// Consumes the code when it matches. Too many wrong codes discard it.
    /// </summary>
    Task<bool> RedeemAsync(string email, string code);
}

public sealed record TestAuthAuthenticatorInvite(string Code, DateTimeOffset ExpiresAtUtc);
