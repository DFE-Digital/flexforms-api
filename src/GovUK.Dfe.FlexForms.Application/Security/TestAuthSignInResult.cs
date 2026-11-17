using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace GovUK.Dfe.FlexForms.Application.Security;

/// <summary>Why a Test Authentication sign-in is refused regardless of the code entered.</summary>
public enum TestAuthLockout
{
    None,

    /// <summary>Too many wrong authenticator app codes. Expires on its own.</summary>
    Temporary,

    /// <summary>An administrator locked the email out. Never expires.</summary>
    Manual
}

/// <summary>Outcome of checking a Test Authentication sign-in code.</summary>
/// <param name="IsValid"><c>true</c> when the code matched and was consumed.</param>
/// <param name="Lockout">Set when the email is locked out now, or will be on the next attempt.</param>
/// <param name="RetryAfter">How long until a <see cref="TestAuthLockout.Temporary"/> lockout expires.</param>
public sealed record TestAuthSignInResult(
    bool IsValid,
    TestAuthLockout Lockout = TestAuthLockout.None,
    TimeSpan? RetryAfter = null)
{
    public static readonly TestAuthSignInResult Valid = new(true);
    public static readonly TestAuthSignInResult Invalid = new(false);
    public static readonly TestAuthSignInResult ManuallyLocked = new(false, TestAuthLockout.Manual);

    public VerifyTestAuthPasswordResponse ToResponse() => new(
        IsValid,
        IsLockedOut: Lockout != TestAuthLockout.None,
        IsPermanentLockout: Lockout == TestAuthLockout.Manual,
        RetryAfterSeconds: Lockout == TestAuthLockout.Temporary ? ToWholeSeconds(RetryAfter) : null);

    /// <summary>Rounds up, so the user is never told they can try again before they can.</summary>
    public static int? ToWholeSeconds(TimeSpan? duration)
        => duration is { } value ? Math.Max(1, (int)Math.Ceiling(value.TotalSeconds)) : null;
}
