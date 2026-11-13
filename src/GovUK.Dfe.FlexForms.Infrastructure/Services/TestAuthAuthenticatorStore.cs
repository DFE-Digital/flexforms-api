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
public sealed class TestAuthAuthenticatorStore(
    TenantConfigDbContext dbContext,
    ITestAuthAuthenticatorSecretProtector secretProtector,
    ILogger<TestAuthAuthenticatorStore> logger) : ITestAuthAuthenticatorStore
{
    public async Task<TestAuthAuthenticatorEnrolment?> FindAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TestAuthAuthenticators
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.TenantId == tenantId && e.NormalizedEmail == normalizedEmail, cancellationToken);

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

    public async Task SaveAsync(
        Guid tenantId,
        string normalizedEmail,
        byte[] secret,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        var encryptedSecret = secretProtector.Protect(secret);
        var entity = await FindTrackedAsync(tenantId, normalizedEmail, cancellationToken);

        if (entity is null)
        {
            dbContext.TestAuthAuthenticators.Add(new TestAuthAuthenticatorEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                NormalizedEmail = normalizedEmail,
                EncryptedSecret = encryptedSecret,
                CreatedAtUtc = createdAtUtc
            });
        }
        else
        {
            entity.EncryptedSecret = encryptedSecret;
            entity.CreatedAtUtc = createdAtUtc;
            entity.LastUsedAtUtc = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkUsedAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTime usedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await FindTrackedAsync(tenantId, normalizedEmail, cancellationToken);
        if (entity is null)
            return;

        entity.LastUsedAtUtc = usedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        var entity = await FindTrackedAsync(tenantId, normalizedEmail, cancellationToken);
        if (entity is null)
            return false;

        dbContext.TestAuthAuthenticators.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<TestAuthAuthenticatorEntity?> FindTrackedAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken)
        => dbContext.TestAuthAuthenticators
            .SingleOrDefaultAsync(e => e.TenantId == tenantId && e.NormalizedEmail == normalizedEmail, cancellationToken);
}
