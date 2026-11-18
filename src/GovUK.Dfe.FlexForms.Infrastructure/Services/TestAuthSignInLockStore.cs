using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Stores administrator locks on Test Authentication sign-in in <c>tenantconfig.TestAuthSignInLocks</c>. The unique
/// (TenantId, NormalizedEmail) index makes two administrators locking the same user at once leave one row.
/// </summary>
public sealed class TestAuthSignInLockStore(TenantConfigDbContext dbContext) : ITestAuthSignInLockStore
{
    public Task<TestAuthSignInLock?> FindAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
        => dbContext.TestAuthSignInLocks
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.NormalizedEmail == normalizedEmail)
            .Select(e => new TestAuthSignInLock(e.LockedAtUtc, e.LockedByEmail))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> LockAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTime lockedAtUtc,
        string lockedByEmail,
        CancellationToken cancellationToken = default)
    {
        if (await FindAsync(tenantId, normalizedEmail, cancellationToken) is not null)
            return false;

        var entity = new TestAuthSignInLockEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NormalizedEmail = normalizedEmail,
            LockedAtUtc = lockedAtUtc,
            LockedByEmail = lockedByEmail
        };
        dbContext.TestAuthSignInLocks.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another administrator locked the same user between the read and the insert: their lock stands.
            dbContext.Entry(entity).State = EntityState.Detached;
            if (await FindAsync(tenantId, normalizedEmail, cancellationToken) is not null)
                return false;

            throw;
        }
    }

    public async Task<bool> UnlockAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.TestAuthSignInLocks
            .SingleOrDefaultAsync(e => e.TenantId == tenantId && e.NormalizedEmail == normalizedEmail, cancellationToken);
        if (entity is null)
            return false;

        dbContext.TestAuthSignInLocks.Remove(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another administrator unlocked the user at the same time: it is unlocked either way.
            dbContext.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }
}
