using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TestAuthSignInLockStoreTests : IDisposable
{
    private const string Email = "tester@education.gov.uk";
    private const string AdminEmail = "admin@education.gov.uk";
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime LockedAt = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);

    private readonly TenantConfigDbContext _db;
    private readonly TestAuthSignInLockStore _store;

    public TestAuthSignInLockStoreTests()
    {
        _db = new TenantConfigDbContext(new DbContextOptionsBuilder<TenantConfigDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _store = new TestAuthSignInLockStore(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task FindAsync_returns_null_when_not_locked()
    {
        Assert.Null(await _store.FindAsync(TenantId, Email));
    }

    [Fact]
    public async Task LockAsync_then_FindAsync_returns_who_and_when()
    {
        Assert.True(await _store.LockAsync(TenantId, Email, LockedAt, AdminEmail));

        var found = await _store.FindAsync(TenantId, Email);

        Assert.NotNull(found);
        Assert.Equal(LockedAt, found.LockedAtUtc);
        Assert.Equal(AdminEmail, found.LockedByEmail);
    }

    [Fact]
    public async Task LockAsync_keeps_the_original_lock_when_already_locked()
    {
        await _store.LockAsync(TenantId, Email, LockedAt, AdminEmail);

        Assert.False(await _store.LockAsync(TenantId, Email, LockedAt.AddHours(1), "other@education.gov.uk"));

        Assert.Equal(AdminEmail, (await _store.FindAsync(TenantId, Email))!.LockedByEmail);
        Assert.Equal(1, await _db.TestAuthSignInLocks.CountAsync());
    }

    [Fact]
    public async Task Locks_are_per_tenant()
    {
        await _store.LockAsync(TenantId, Email, LockedAt, AdminEmail);

        Assert.Null(await _store.FindAsync(OtherTenantId, Email));
        Assert.False(await _store.UnlockAsync(OtherTenantId, Email));
        Assert.NotNull(await _store.FindAsync(TenantId, Email));
    }

    [Fact]
    public async Task UnlockAsync_removes_the_lock_once()
    {
        await _store.LockAsync(TenantId, Email, LockedAt, AdminEmail);

        Assert.True(await _store.UnlockAsync(TenantId, Email));
        Assert.False(await _store.UnlockAsync(TenantId, Email));
        Assert.Null(await _store.FindAsync(TenantId, Email));
    }
}
