using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Security;

/// <inheritdoc />
public sealed class TestAuthPasswordService(
    IAdvancedRedisCacheService redisCacheService,
    ITenantContextAccessor tenantContextAccessor,
    TimeProvider timeProvider,
    ILogger<TestAuthPasswordService> logger) : ITestAuthPasswordService
{
    public static readonly TimeSpan PasswordLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);
    public const int MaxFailedAttempts = 5;

    private const int PasswordUpperBoundExclusive = 1_000_000;
    private const string CacheKeyPrefix = "TestAuthPassword_";

    public async Task<string?> IssueAsync(string email)
    {
        var cacheKey = CreateCacheKey(email);
        var now = timeProvider.GetUtcNow();

        var existing = await ReadAsync(cacheKey);
        if (existing is not null && now - existing.IssuedAtUtc < ResendCooldown)
        {
            logger.LogInformation(
                "Test authentication password for {Email} was issued less than {Cooldown} ago; not issuing a new one",
                email,
                ResendCooldown);
            return null;
        }

        var password = RandomNumberGenerator.GetInt32(PasswordUpperBoundExclusive)
            .ToString("D6", CultureInfo.InvariantCulture);

        await WriteAsync(
            cacheKey,
            new TestAuthPasswordEntry(Hash(password), now, now + PasswordLifetime, 0),
            PasswordLifetime);

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
            await redisCacheService.RemoveAsync(cacheKey);
            return false;
        }

        if (HashesMatch(entry.PasswordHash, Hash(password.Trim())))
        {
            await redisCacheService.RemoveAsync(cacheKey);
            return true;
        }

        var failedAttempts = entry.FailedAttempts + 1;
        if (failedAttempts >= MaxFailedAttempts)
        {
            logger.LogWarning(
                "Test authentication password for {Email} invalidated after {FailedAttempts} failed attempts",
                email,
                failedAttempts);
            await redisCacheService.RemoveAsync(cacheKey);
            return false;
        }

        await WriteAsync(cacheKey, entry with { FailedAttempts = failedAttempts }, entry.ExpiresAtUtc - now);
        return false;
    }

    private string CreateCacheKey(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"{CacheKeyPrefix}{CacheKeyHelper.GenerateHashedCacheKey(normalizedEmail)}");
    }

    private async Task<TestAuthPasswordEntry?> ReadAsync(string cacheKey)
    {
        var raw = await redisCacheService.GetRawAsync(cacheKey);
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
            await redisCacheService.RemoveAsync(cacheKey);
            return null;
        }
    }

    private Task WriteAsync(string cacheKey, TestAuthPasswordEntry entry, TimeSpan expiry)
        => redisCacheService.SetRawAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(entry), expiry);

    private static string Hash(string password)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    private static bool HashesMatch(string expected, string actual)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(actual));

    private sealed record TestAuthPasswordEntry(
        string PasswordHash,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        int FailedAttempts);
}
