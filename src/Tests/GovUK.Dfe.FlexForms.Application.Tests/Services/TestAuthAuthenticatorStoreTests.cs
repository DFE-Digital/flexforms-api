using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TestAuthAuthenticatorStoreTests : IDisposable
{
    private const string Email = "tester@education.gov.uk";
    private const string FirstSetup = "11111111111111111111111111111111";
    private const string SecondSetup = "22222222222222222222222222222222";
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

        Assert.True(await _store.SaveAsync(TenantId, Email, secret, CreatedAt, FirstSetup, expectedCurrentSetupId: null));
        var found = await _store.FindAsync(TenantId, Email);

        Assert.NotNull(found);
        Assert.Equal(secret, found.Secret);
        Assert.Equal(CreatedAt, found.CreatedAtUtc);
        Assert.Equal(FirstSetup, await _store.GetCurrentSetupIdAsync(TenantId, Email));
    }

    [Fact]
    public async Task SaveAsync_does_not_store_plaintext_secret()
    {
        byte[] secret = [10, 20, 30, 40, 50, 60];

        await _store.SaveAsync(TenantId, Email, secret, CreatedAt, FirstSetup, expectedCurrentSetupId: null);

        var row = await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync();
        Assert.NotEqual(Convert.ToBase64String(secret), row.EncryptedSecret);
        Assert.Equal(Email, row.NormalizedEmail);
    }

    [Fact]
    public async Task SaveAsync_replaces_the_expected_enrolment_and_clears_last_used()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);
        await _store.MarkUsedAsync(TenantId, Email, CreatedAt.AddMinutes(5));

        Assert.True(await _store.SaveAsync(TenantId, Email, [2], CreatedAt.AddDays(1), SecondSetup, FirstSetup));

        var row = await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync();
        Assert.Null(row.LastUsedAtUtc);
        Assert.Equal(CreatedAt.AddDays(1), row.CreatedAtUtc);
        Assert.Equal(SecondSetup, row.SetupId);
        Assert.Equal(new byte[] { 2 }, (await _store.FindAsync(TenantId, Email))!.Secret);
    }

    [Fact]
    public async Task SaveAsync_refuses_to_overwrite_an_enrolment_it_did_not_expect()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);
        await _store.SaveAsync(TenantId, Email, [2], CreatedAt, SecondSetup, FirstSetup);

        Assert.False(await _store.SaveAsync(TenantId, Email, [3], CreatedAt, "33333333333333333333333333333333", FirstSetup));

        Assert.Equal(new byte[] { 2 }, (await _store.FindAsync(TenantId, Email))!.Secret);
    }

    [Fact]
    public async Task SaveAsync_expecting_no_enrolment_refuses_when_one_exists()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);

        Assert.False(await _store.SaveAsync(TenantId, Email, [2], CreatedAt, SecondSetup, expectedCurrentSetupId: null));

        Assert.Equal(new byte[] { 1 }, (await _store.FindAsync(TenantId, Email))!.Secret);
    }

    [Fact]
    public async Task SaveAsync_does_not_recreate_a_removed_enrolment()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);
        await _store.DeleteAsync(TenantId, Email, CreatedAt.AddHours(1));

        Assert.False(await _store.SaveAsync(TenantId, Email, [2], CreatedAt, SecondSetup, FirstSetup));

        Assert.Null(await _store.FindAsync(TenantId, Email));
    }

    [Fact]
    public async Task SaveAsync_first_time_save_refuses_after_an_enrolment_was_created_and_removed()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);
        await _store.DeleteAsync(TenantId, Email, CreatedAt.AddHours(1));

        Assert.False(await _store.SaveAsync(TenantId, Email, [2], CreatedAt, SecondSetup, expectedCurrentSetupId: null));

        Assert.Null(await _store.FindAsync(TenantId, Email));
    }

    [Fact]
    public async Task DeleteAsync_keeps_a_tombstone_with_a_new_generation_and_no_secret()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);

        Assert.True(await _store.DeleteAsync(TenantId, Email, CreatedAt.AddHours(1)));

        var row = await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync();
        Assert.Equal(string.Empty, row.EncryptedSecret);
        Assert.Equal(CreatedAt.AddHours(1), row.RemovedAtUtc);
        Assert.NotEqual(FirstSetup, row.SetupId);
        Assert.Equal(row.SetupId, await _store.GetCurrentSetupIdAsync(TenantId, Email));
        Assert.Null(await _store.FindAsync(TenantId, Email));
    }

    [Fact]
    public async Task SaveAsync_expecting_the_removal_generation_enrols_again()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);
        await _store.DeleteAsync(TenantId, Email, CreatedAt.AddHours(1));
        var removalGeneration = await _store.GetCurrentSetupIdAsync(TenantId, Email);

        Assert.True(await _store.SaveAsync(TenantId, Email, [2], CreatedAt.AddDays(1), SecondSetup, removalGeneration));

        Assert.Equal(new byte[] { 2 }, (await _store.FindAsync(TenantId, Email))!.Secret);
        Assert.Null((await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync()).RemovedAtUtc);
    }

    [Fact]
    public void SetupId_is_a_concurrency_token_so_updates_and_deletes_are_conditional_in_sql()
    {
        var property = _db.Model.FindEntityType(typeof(TestAuthAuthenticatorEntity))!
            .FindProperty(nameof(TestAuthAuthenticatorEntity.SetupId))!;

        Assert.True(property.IsConcurrencyToken);
    }

    [Fact]
    public async Task GetCurrentSetupIdAsync_returns_null_without_enrolment_and_id_even_when_undecryptable()
    {
        Assert.Null(await _store.GetCurrentSetupIdAsync(TenantId, Email));

        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);
        var storeWithOtherKeyRing = CreateStore(new EphemeralDataProtectionProvider());

        Assert.Null(await storeWithOtherKeyRing.FindAsync(TenantId, Email));
        Assert.Equal(FirstSetup, await storeWithOtherKeyRing.GetCurrentSetupIdAsync(TenantId, Email));
    }

    [Fact]
    public async Task FindAsync_is_scoped_to_tenant()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);

        Assert.Null(await _store.FindAsync(Guid.NewGuid(), Email));
    }

    [Fact]
    public async Task MarkUsedAsync_records_last_use()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);

        await _store.MarkUsedAsync(TenantId, Email, CreatedAt.AddMinutes(3));

        Assert.Equal(CreatedAt.AddMinutes(3), (await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync()).LastUsedAtUtc);
    }

    [Fact]
    public async Task DeleteAsync_removes_enrolment_once()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);

        Assert.True(await _store.DeleteAsync(TenantId, Email, CreatedAt.AddHours(1)));
        Assert.False(await _store.DeleteAsync(TenantId, Email, CreatedAt.AddHours(2)));
        Assert.Null(await _store.FindAsync(TenantId, Email));
    }

    [Fact]
    public async Task DeleteAsync_without_enrolment_still_advances_the_generation()
    {
        Assert.False(await _store.DeleteAsync(TenantId, Email, CreatedAt));

        var tombstone = await _store.GetCurrentSetupIdAsync(TenantId, Email);
        Assert.NotNull(tombstone);
        Assert.False(await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null));
        Assert.Null(await _store.FindAsync(TenantId, Email));

        Assert.False(await _store.DeleteAsync(TenantId, Email, CreatedAt.AddMinutes(1)));
        Assert.NotEqual(tombstone, await _store.GetCurrentSetupIdAsync(TenantId, Email));
    }

    [Fact]
    public async Task MarkUsedAsync_ignores_a_removed_enrolment()
    {
        await _store.SaveAsync(TenantId, Email, [1], CreatedAt, FirstSetup, expectedCurrentSetupId: null);
        await _store.DeleteAsync(TenantId, Email, CreatedAt.AddHours(1));

        await _store.MarkUsedAsync(TenantId, Email, CreatedAt.AddHours(2));

        Assert.Null((await _db.TestAuthAuthenticators.AsNoTracking().SingleAsync()).LastUsedAtUtc);
    }

    private TestAuthAuthenticatorStore CreateStore(IDataProtectionProvider dataProtection)
        => new(
            _db,
            new DataProtectionTestAuthAuthenticatorSecretProtector(dataProtection),
            NullLogger<TestAuthAuthenticatorStore>.Instance);
}
