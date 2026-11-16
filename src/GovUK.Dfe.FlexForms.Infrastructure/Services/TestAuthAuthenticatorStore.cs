using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Stores Test Authentication authenticator app enrolments in <c>tenantconfig.TestAuthAuthenticators</c>.
/// Losing the Data Protection key ring makes existing secrets unreadable; those users sign in with the emailed
/// password and enrol again.
/// </summary>
/// <remarks>
/// <see cref="TestAuthAuthenticatorEntity.SetupId"/> is the tenant/email generation and a concurrency token, so every
/// update is <c>WHERE SetupId = (value read)</c>, and the unique (TenantId, NormalizedEmail) index stops two first-time
/// saves. That makes <see cref="SaveAsync"/> a compare-and-swap across replicas. Removal keeps the row as a tombstone
/// with a new generation instead of deleting it: a deleted row would look like "never enrolled" again, letting a
/// first-time setup that began before an enrol-then-remove recreate an authenticator.
/// </remarks>
public sealed class TestAuthAuthenticatorStore(
    TenantConfigDbContext dbContext,
    ITestAuthAuthenticatorSecretProtector secretProtector,
    ILogger<TestAuthAuthenticatorStore> logger) : ITestAuthAuthenticatorStore
{
    private const int MaxDeleteAttempts = 3;

    public async Task<TestAuthAuthenticatorEnrolment?> FindAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TestAuthAuthenticators
            .AsNoTracking()
            .SingleOrDefaultAsync(
                e => e.TenantId == tenantId && e.NormalizedEmail == normalizedEmail && e.RemovedAtUtc == null,
                cancellationToken);

        if (entity is null)
            return null;

        var secret = secretProtector.Unprotect(entity.EncryptedSecret);
        if (secret is null)
        {
            logger.LogWarning(
                "Test authentication authenticator {AuthenticatorId} in tenant {TenantId} could not be decrypted and is ignored",
                entity.Id,
                tenantId);
            return null;
        }

        return new TestAuthAuthenticatorEnrolment(secret, entity.CreatedAtUtc);
    }

    public Task<string?> GetCurrentSetupIdAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
        => dbContext.TestAuthAuthenticators
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.NormalizedEmail == normalizedEmail)
            .Select(e => e.SetupId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> SaveAsync(
        Guid tenantId,
        string normalizedEmail,
        byte[] secret,
        DateTime createdAtUtc,
        string setupId,
        string? expectedCurrentSetupId,
        CancellationToken cancellationToken = default)
    {
        var encryptedSecret = secretProtector.Protect(secret);
        var entity = await FindTrackedAsync(tenantId, normalizedEmail, cancellationToken);

        if (expectedCurrentSetupId is null)
        {
            if (entity is not null)
                return Stale(tenantId);

            dbContext.TestAuthAuthenticators.Add(new TestAuthAuthenticatorEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                NormalizedEmail = normalizedEmail,
                EncryptedSecret = encryptedSecret,
                SetupId = setupId,
                CreatedAtUtc = createdAtUtc
            });
        }
        else
        {
            if (entity is null || entity.SetupId != expectedCurrentSetupId)
                return Stale(tenantId);

            entity.EncryptedSecret = encryptedSecret;
            entity.SetupId = setupId;
            entity.CreatedAtUtc = createdAtUtc;
            entity.LastUsedAtUtc = null;
            entity.RemovedAtUtc = null;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return Stale(tenantId);
        }
        catch (DbUpdateException) when (expectedCurrentSetupId is null)
        {
            // Another first-time save won the unique index. Anything else is a real failure.
            dbContext.ChangeTracker.Clear();
            if (await GetCurrentSetupIdAsync(tenantId, normalizedEmail, cancellationToken) is null)
                throw;

            return Stale(tenantId);
        }
    }

    public async Task MarkUsedAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTime usedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await FindTrackedAsync(tenantId, normalizedEmail, cancellationToken);
        if (entity is null || entity.RemovedAtUtc is not null)
            return;

        entity.LastUsedAtUtc = usedAtUtc;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTime removedAtUtc,
        CancellationToken cancellationToken = default)
    {
        // Always advances the generation, even when nothing is enrolled, so a setup already being confirmed (including
        // a first-time one) cannot enrol after the user asked to remove their authenticator.
        for (var attempt = 1; ; attempt++)
        {
            var entity = await FindTrackedAsync(tenantId, normalizedEmail, cancellationToken);
            var wasActive = entity is { RemovedAtUtc: null };

            if (entity is null)
            {
                dbContext.TestAuthAuthenticators.Add(new TestAuthAuthenticatorEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    NormalizedEmail = normalizedEmail,
                    EncryptedSecret = string.Empty,
                    SetupId = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = removedAtUtc,
                    RemovedAtUtc = removedAtUtc
                });
            }
            else
            {
                entity.EncryptedSecret = string.Empty;
                entity.SetupId = Guid.NewGuid().ToString("N");
                entity.RemovedAtUtc ??= removedAtUtc;
                entity.LastUsedAtUtc = null;
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return wasActive;
            }
            catch (DbUpdateException) when (attempt < MaxDeleteAttempts)
            {
                // Changed between read and write (or a concurrent first insert won): apply to whatever is there now.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private bool Stale(Guid tenantId)
    {
        logger.LogWarning(
            "Test authentication authenticator enrolment in tenant {TenantId} changed while a setup was being confirmed; the older setup was not saved",
            tenantId);
        return false;
    }

    private Task<TestAuthAuthenticatorEntity?> FindTrackedAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken)
        => dbContext.TestAuthAuthenticators
            .SingleOrDefaultAsync(e => e.TenantId == tenantId && e.NormalizedEmail == normalizedEmail, cancellationToken);
}
