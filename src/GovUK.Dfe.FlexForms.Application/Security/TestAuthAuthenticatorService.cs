using System.Globalization;
using System.Text;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Caching;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace GovUK.Dfe.FlexForms.Application.Security;

/// <inheritdoc />
/// <remarks>
/// RFC 6238 TOTP with the settings every mainstream authenticator app expects: SHA1, 6 digits, 30 second step, and
/// one step of clock drift either side. Counters, the pending enrolment and used codes follow the same
/// <see cref="IAtomicCacheStore"/> rules as <see cref="TestAuthPasswordService"/>: every attempt is counted before
/// the code is compared, the pending enrolment is only consumed by compare-and-delete against the exact entry read,
/// and a code is marked used with a single SET NX so parallel requests cannot both sign in with it.
/// </remarks>
public sealed class TestAuthAuthenticatorService(
    IAtomicCacheStore store,
    ITestAuthAuthenticatorStore authenticatorStore,
    ITestAuthAuthenticatorSecretProtector secretProtector,
    ITenantContextAccessor tenantContextAccessor,
    TimeProvider timeProvider,
    ILogger<TestAuthAuthenticatorService> logger) : ITestAuthAuthenticatorService
{
    public static readonly TimeSpan PendingSetupLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);
    public const int MaxFailedAttempts = 5;

    private const int SecretLengthBytes = 20;
    private const int StepSeconds = 30;
    private const int CodeLength = 6;
    private static readonly VerificationWindow DriftWindow = new(previous: 1, future: 1);
    private static readonly TimeSpan UsedCodeLifetime = TimeSpan.FromSeconds(StepSeconds * 4);

    private const string CacheKeyPrefix = "TestAuthAuthenticator_";
    private const string SetupSuffix = ":setup";
    private const string AttemptsSuffix = ":attempts";
    private const string FailuresSuffix = ":failures";
    private const string StepSuffix = ":step:";

    public async Task<DateTime?> GetEnrolledOnAsync(string email, CancellationToken cancellationToken = default)
    {
        var enrolment = await authenticatorStore.FindAsync(TenantId, Normalize(email), cancellationToken);
        return enrolment?.CreatedAtUtc;
    }

    public Task<TestAuthAuthenticatorSetup> BeginSetupAsync(
        string email,
        string issuer,
        CancellationToken cancellationToken = default)
        => BeginSetupCoreAsync(email, issuer, fromInvite: false, cancellationToken);

    public Task<TestAuthAuthenticatorSetup> BeginInviteSetupAsync(
        string email,
        string issuer,
        CancellationToken cancellationToken = default)
        => BeginSetupCoreAsync(email, issuer, fromInvite: true, cancellationToken);

    public Task<bool> ConfirmSetupAsync(string email, string code, CancellationToken cancellationToken = default)
        => ConfirmSetupCoreAsync(email, code, inviteOnly: false, cancellationToken);

    public Task<bool> ConfirmInviteSetupAsync(string email, string code, CancellationToken cancellationToken = default)
        => ConfirmSetupCoreAsync(email, code, inviteOnly: true, cancellationToken);

    private async Task<TestAuthAuthenticatorSetup> BeginSetupCoreAsync(
        string email,
        string issuer,
        bool fromInvite,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = Normalize(email);
        var secret = KeyGeneration.GenerateRandomKey(SecretLengthBytes);

        // The enrolment this setup will replace. Confirming only saves while it is still current, so a confirmation
        // that is delayed cannot overwrite a newer enrolment or bring back one that was removed.
        var replacesSetupId = await authenticatorStore.GetCurrentSetupIdAsync(TenantId, normalizedEmail, cancellationToken);

        // A fresh id gives this pending secret its own attempt counter, so starting again resets nothing shared.
        await store.SetAsync(
            CreateCacheKey(normalizedEmail) + SetupSuffix,
            JsonSerializer.SerializeToUtf8Bytes(new PendingSetupEntry(
                secretProtector.Protect(secret),
                Guid.NewGuid().ToString("N"),
                fromInvite,
                replacesSetupId)),
            PendingSetupLifetime);

        var sharedKey = Base32Encoding.ToString(secret);
        return new TestAuthAuthenticatorSetup(sharedKey, BuildAuthenticatorUri(issuer, normalizedEmail, sharedKey));
    }

    private async Task<bool> ConfirmSetupCoreAsync(
        string email,
        string code,
        bool inviteOnly,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = Normalize(email);
        var cacheKey = CreateCacheKey(normalizedEmail);
        var setupKey = cacheKey + SetupSuffix;

        var raw = await store.GetAsync(setupKey);
        if (raw is null)
            return false;

        var entry = TryRead(raw);
        var secret = entry is null ? null : secretProtector.Unprotect(entry.ProtectedSecret);
        if (entry is null || secret is null)
        {
            logger.LogWarning("Discarding unreadable pending test authentication authenticator enrolment");
            await store.DeleteIfEqualsAsync(setupKey, raw);
            return false;
        }

        // Checked before counting, so anonymous attempts cannot use up a signed-in user's setup attempts.
        if (inviteOnly && !entry.FromInvite)
            return false;

        var attemptsKey = $"{setupKey}{AttemptsSuffix}:{entry.Id}";
        var attempts = await store.IncrementAsync(attemptsKey, PendingSetupLifetime);
        if (attempts > MaxFailedAttempts)
        {
            await InvalidateSetupAsync(setupKey, raw, attemptsKey);
            return false;
        }

        if (!TryMatch(secret, code, out var timeStep))
        {
            if (attempts >= MaxFailedAttempts)
            {
                logger.LogWarning(
                    "Pending test authentication authenticator enrolment for {Email} discarded after {FailedAttempts} wrong codes",
                    normalizedEmail,
                    attempts);
                await InvalidateSetupAsync(setupKey, raw, attemptsKey);
            }

            return false;
        }

        // Only the request still holding the exact pending entry it read may save it.
        if (!await store.DeleteIfEqualsAsync(setupKey, raw))
            return false;

        await store.DeleteAsync(attemptsKey);
        await store.SetIfAbsentAsync(cacheKey + StepSuffix + timeStep.ToString(CultureInfo.InvariantCulture), [1], UsedCodeLifetime);

        var saved = await authenticatorStore.SaveAsync(
            TenantId,
            normalizedEmail,
            secret,
            timeProvider.GetUtcNow().UtcDateTime,
            entry.Id,
            entry.ReplacesSetupId,
            cancellationToken);
        if (!saved)
            return false;

        logger.LogInformation("Test authentication authenticator enrolled for {Email}", normalizedEmail);
        return true;
    }

    public async Task<bool> RemoveAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = Normalize(email);
        await store.DeleteAsync(CreateCacheKey(normalizedEmail) + SetupSuffix);

        var removed = await authenticatorStore.DeleteAsync(
            TenantId,
            normalizedEmail,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        if (removed)
            logger.LogInformation("Test authentication authenticator removed for {Email}", normalizedEmail);

        return removed;
    }

    public async Task<bool> VerifyAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = Normalize(email);
        var cacheKey = CreateCacheKey(normalizedEmail);
        var failuresKey = cacheKey + FailuresSuffix;

        // Counted before anything is compared (including for emails with no enrolment), so the number of guesses
        // is capped even when several arrive at once, and the response does not reveal who has enrolled.
        var attempts = await store.IncrementAsync(failuresKey, LockoutWindow);
        if (attempts > MaxFailedAttempts)
        {
            logger.LogWarning(
                "Test authentication authenticator sign-in for {Email} refused: locked out after {FailedAttempts} attempts",
                normalizedEmail,
                MaxFailedAttempts);
            return false;
        }

        var enrolment = await authenticatorStore.FindAsync(TenantId, normalizedEmail, cancellationToken);
        if (enrolment is null || !TryMatch(enrolment.Secret, code, out var timeStep))
            return false;

        if (!await store.SetIfAbsentAsync(cacheKey + StepSuffix + timeStep.ToString(CultureInfo.InvariantCulture), [1], UsedCodeLifetime))
        {
            logger.LogWarning("Test authentication authenticator code for {Email} was already used", normalizedEmail);
            return false;
        }

        await store.DeleteAsync(failuresKey);
        await authenticatorStore.MarkUsedAsync(TenantId, normalizedEmail, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return true;
    }

    /// <summary>Builds the <c>otpauth://</c> URI authenticator apps read from the QR code.</summary>
    public static string BuildAuthenticatorUri(string issuer, string email, string sharedKey)
    {
        // A colon separates issuer from account in the label, so it cannot appear in the issuer itself.
        var safeIssuer = Uri.EscapeDataString(issuer.Replace(':', ' ').Trim());
        var account = Uri.EscapeDataString(email);

        return new StringBuilder("otpauth://totp/")
            .Append(safeIssuer).Append(':').Append(account)
            .Append("?secret=").Append(sharedKey)
            .Append("&issuer=").Append(safeIssuer)
            .Append("&algorithm=SHA1&digits=").Append(CodeLength.ToString(CultureInfo.InvariantCulture))
            .Append("&period=").Append(StepSeconds.ToString(CultureInfo.InvariantCulture))
            .ToString();
    }

    private bool TryMatch(byte[] secret, string code, out long timeStep)
    {
        timeStep = 0;
        var trimmed = code.Trim();
        if (trimmed.Length != CodeLength || !trimmed.All(char.IsAsciiDigit))
            return false;

        var totp = new Totp(secret, StepSeconds, OtpHashMode.Sha1, CodeLength);
        return totp.VerifyTotp(timeProvider.GetUtcNow().UtcDateTime, trimmed, out timeStep, DriftWindow);
    }

    private async Task InvalidateSetupAsync(string setupKey, byte[] raw, string attemptsKey)
    {
        await store.DeleteIfEqualsAsync(setupKey, raw);
        await store.DeleteAsync(attemptsKey);
    }

    private static PendingSetupEntry? TryRead(byte[] raw)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingSetupEntry>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Guid TenantId => tenantContextAccessor.CurrentTenant?.Id
        ?? throw new InvalidOperationException("Test authentication authenticator requires a tenant on the current context.");

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private string CreateCacheKey(string normalizedEmail)
        => TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"{CacheKeyPrefix}{CacheKeyHelper.GenerateHashedCacheKey(normalizedEmail)}");

    /// <param name="ReplacesSetupId">Setup id of the enrolment current when this setup began, or <c>null</c> if none.</param>
    private sealed record PendingSetupEntry(
        string ProtectedSecret,
        string Id,
        bool FromInvite = false,
        string? ReplacesSetupId = null);
}
