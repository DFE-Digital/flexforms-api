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
/// Only a SHA-256 hash of the code is stored. Each code has its own attempt counter, every attempt is counted before
/// the code is compared, and a matching code is consumed by compare-and-delete against the exact entry read, so two
/// parallel requests cannot both use it (same <see cref="IAtomicCacheStore"/> rules as <see cref="TestAuthPasswordService"/>).
/// </remarks>
public sealed class TestAuthAuthenticatorInviteService(
    IAtomicCacheStore store,
    ITenantContextAccessor tenantContextAccessor,
    TimeProvider timeProvider,
    ILogger<TestAuthAuthenticatorInviteService> logger) : ITestAuthAuthenticatorInviteService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(24);
    public const int MaxFailedAttempts = 5;

    // No 0/O or 1/I, so codes read out or copied by hand are not misread.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 10;
    private const string CacheKeyPrefix = "TestAuthAuthenticatorInvite_";
    private const string AttemptsSuffix = ":attempts:";

    public async Task<TestAuthAuthenticatorInvite> IssueAsync(string email)
    {
        var code = RandomNumberGenerator.GetString(Alphabet, CodeLength);
        var expiresAt = timeProvider.GetUtcNow() + CodeLifetime;

        await store.SetAsync(
            CreateCacheKey(email),
            JsonSerializer.SerializeToUtf8Bytes(new InviteEntry(Hash(code), Guid.NewGuid().ToString("N"))),
            CodeLifetime);

        return new TestAuthAuthenticatorInvite($"{code[..5]}-{code[5..]}", expiresAt);
    }

    public async Task<bool> RedeemAsync(string email, string code)
    {
        var cacheKey = CreateCacheKey(email);
        var raw = await store.GetAsync(cacheKey);
        if (raw is null)
            return false;

        var entry = TryRead(raw);
        if (entry is null)
        {
            logger.LogWarning("Discarding unreadable test authentication authenticator setup code");
            await store.DeleteIfEqualsAsync(cacheKey, raw);
            return false;
        }

        var attemptsKey = cacheKey + AttemptsSuffix + entry.Id;
        var attempts = await store.IncrementAsync(attemptsKey, CodeLifetime);
        if (attempts > MaxFailedAttempts)
        {
            await InvalidateAsync(cacheKey, raw, attemptsKey);
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(entry.CodeHash),
                Encoding.ASCII.GetBytes(Hash(Normalize(code)))))
        {
            if (attempts >= MaxFailedAttempts)
            {
                logger.LogWarning(
                    "Test authentication authenticator setup code for {Email} discarded after {FailedAttempts} wrong attempts",
                    email,
                    attempts);
                await InvalidateAsync(cacheKey, raw, attemptsKey);
            }

            return false;
        }

        var consumed = await store.DeleteIfEqualsAsync(cacheKey, raw);
        await store.DeleteAsync(attemptsKey);
        return consumed;
    }

    private async Task InvalidateAsync(string cacheKey, byte[] raw, string attemptsKey)
    {
        await store.DeleteIfEqualsAsync(cacheKey, raw);
        await store.DeleteAsync(attemptsKey);
    }

    private static InviteEntry? TryRead(byte[] raw)
    {
        try
        {
            return JsonSerializer.Deserialize<InviteEntry>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Normalize(string code)
        => new(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private string CreateCacheKey(string email)
        => TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"{CacheKeyPrefix}{CacheKeyHelper.GenerateHashedCacheKey(email.Trim().ToLowerInvariant())}");

    private sealed record InviteEntry(string CodeHash, string Id);
}
