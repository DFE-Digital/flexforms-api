using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TestAuthAuthenticatorStoreTests : IDisposable
{
    private const string Email = "tester@education.gov.uk";
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTime CreatedAt = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly TenantConfigDbContext _db;
    private readonly EphemeralDataProtectionProvider _dataProtection = new();
    private readonly TestAuthAuthenticatorStore _store;

    public TestAuthAuthenticatorStoreTests()
    {
        _db = new TenantConfigDbContext(new DbContextOptionsBuilder<TenantConfigDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _store = CreateStore(_dataProtection);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SaveAsync_then_FindAsync_round_trips_secret()
    {
        byte[] secret = [1, 2, 3, 4, 5];

        await _store.SaveAsync(TenantId, Email, secret, CreatedAt);
        var found = await _store.FindAsync(TenantId, Email);

        Assert.NotNull(found);
        Assert.Equal(secret, found.Secret);
        Assert.Equal(CreatedAt, found.CreatedAtUtc);
    }

    [Fact]
    public async Task SaveAsync_does_not_store_plaintext_secret()
    {
        byte[] secret = [10, 20, 30, 40, 50, 60];

        await _store.SaveAsync(TenantId, Email, secret, CreatedAt);

        var row = await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync();
        Assert.NotEqual(Convert.ToBase64String(secret), row.EncryptedSecret);
        Assert.Equal(Email, row.NormalizedEmail);
    }

    [Fact]
    public async Task SaveAsync_replaces_existing_enrolment_and_clears_last_used()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt);
        await _store.MarkUsedAsync(TenantId, Email, CreatedAt.AddMinutes(5));

        await _store.SaveAsync(TenantId, Email, [2], CreatedAt.AddDays(1));

        var row = await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync();
        Assert.Null(row.LastUsedAtUtc);
        Assert.Equal(CreatedAt.AddDays(1), row.CreatedAtUtc);
        Assert.Equal(new byte[] { 2 }, (await _store.FindAsync(TenantId, Email))!.Secret);
    }

    [Fact]
    public async Task FindAsync_is_scoped_to_tenant()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt);

        Assert.Null(await _store.FindAsync(Guid.NewGuid(), Email));
    }

    [Fact]
    public async Task FindAsync_returns_null_when_secret_cannot_be_decrypted()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt);

        var storeWithOtherKeyRing = CreateStore(new EphemeralDataProtectionProvider());

        Assert.Null(await storeWithOtherKeyRing.FindAsync(TenantId, Email));
    }

    [Fact]
    public async Task MarkUsedAsync_records_last_use()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt);

        await _store.MarkUsedAsync(TenantId, Email, CreatedAt.AddMinutes(3));

        Assert.Equal(CreatedAt.AddMinutes(3), (await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync()).LastUsedAtUtc);
    }

    [Fact]
    public async Task DeleteAsync_removes_enrolment_once()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt);

        Assert.True(await _store.DeleteAsync(TenantId, Email));
        Assert.False(await _store.DeleteAsync(TenantId, Email));
        Assert.Empty(_db.TestAuthAuthenticators);
    }

    private TestAuthAuthenticatorStore CreateStore(IDataProtectionProvider dataProtection)
        => new(
            _db,
            new DataProtectionTestAuthAuthenticatorSecretProtector(dataProtection),
            NullLogger<TestAuthAuthenticatorStore>.Instance);
}
