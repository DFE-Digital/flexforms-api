using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Caching;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Security;

/// <inheritdoc />
/// <remarks>
/// The resend cooldown, the attempt count and consuming a correct password are each a single atomic Redis
/// operation, so parallel requests (on one replica or several) cannot exceed <see cref="MaxFailedAttempts"/>
/// or both sign in with the same password.
/// </remarks>
public sealed class TestAuthPasswordService(
    IAtomicCacheStore store,
    ITenantContextAccessor tenantContextAccessor,
    TimeProvider timeProvider,
    ILogger<TestAuthPasswordService> logger) : ITestAuthPasswordService
{
    public static readonly TimeSpan PasswordLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);
    public const int MaxFailedAttempts = 5;

    private const int PasswordUpperBoundExclusive = 1_000_000;
    private const string CacheKeyPrefix = "TestAuthPassword_";
    private const string CooldownSuffix = ":cooldown";
    private const string AttemptsSuffix = ":attempts";

    public async Task<string?> IssueAsync(string email)
    {
        var cacheKey = CreateCacheKey(email);

        if (!await store.SetIfAbsentAsync(cacheKey + CooldownSuffix, [1], ResendCooldown))
        {
            logger.LogInformation(
                "Test authentication password for {Email} was issued less than {Cooldown} ago; not issuing a new one",
                email,
                ResendCooldown);
            return null;
        }

        var password = RandomNumberGenerator.GetInt32(PasswordUpperBoundExclusive)
            .ToString("D6", CultureInfo.InvariantCulture);
        var now = timeProvider.GetUtcNow();

        await store.SetAsync(
            cacheKey,
            JsonSerializer.SerializeToUtf8Bytes(new TestAuthPasswordEntry(Hash(password), now, now + PasswordLifetime)),
            PasswordLifetime);
        await store.DeleteAsync(cacheKey + AttemptsSuffix);

        return password;
    }

    public async Task<bool> VerifyAsync(string email, string password)
    {
        var cacheKey = CreateCacheKey(email);
        var entry = await ReadAsync(cacheKey);
        if (entry is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        if (now >= entry.ExpiresAtUtc)
        {
            await InvalidateAsync(cacheKey);
            return false;
        }

        // Every attempt is counted before the password is compared, so the number of guesses is capped
        // even when several arrive at once.
        var attempts = await store.IncrementAsync(cacheKey + AttemptsSuffix, entry.ExpiresAtUtc - now);
        if (attempts > MaxFailedAttempts)
        {
            await InvalidateAsync(cacheKey);
            return false;
        }

        if (HashesMatch(entry.PasswordHash, Hash(password.Trim())))
        {
            var consumed = await store.DeleteAsync(cacheKey);
            await store.DeleteAsync(cacheKey + AttemptsSuffix);
            return consumed;
        }

        if (attempts >= MaxFailedAttempts)
        {
            logger.LogWarning(
                "Test authentication password for {Email} invalidated after {FailedAttempts} failed attempts",
                email,
                attempts);
            await InvalidateAsync(cacheKey);
        }

        return false;
    }

    private string CreateCacheKey(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"{CacheKeyPrefix}{CacheKeyHelper.GenerateHashedCacheKey(normalizedEmail)}");
    }

    private async Task InvalidateAsync(string cacheKey)
    {
        await store.DeleteAsync(cacheKey);
        await store.DeleteAsync(cacheKey + AttemptsSuffix);
    }

    private async Task<TestAuthPasswordEntry?> ReadAsync(string cacheKey)
    {
        var raw = await store.GetAsync(cacheKey);
        if (raw is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TestAuthPasswordEntry>(raw);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Discarding unreadable test authentication password entry");
            await store.DeleteAsync(cacheKey);
            return null;
        }
    }

    private static string Hash(string password)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    private static bool HashesMatch(string expected, string actual)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(actual));

    private sealed record TestAuthPasswordEntry(
        string PasswordHash,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc);
}
