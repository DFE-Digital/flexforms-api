using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantSettingTargetOverlapGateTests : IDisposable
{
    private static readonly Guid SourceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid NewId = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private const string InternalAuthJson = """{"SecretKey":"old-internal-secret-key-32chars!","Issuer":"iss","Audience":"aud"}""";
    private const string TestAuthJson = """{"Enabled":true,"JwtSigningKey":"old","JwtIssuer":"old","JwtAudience":"old"}""";

    private readonly TenantConfigDbContext _db;
    private readonly ITenantSettingsEncryptor _encryptor = Substitute.For<ITenantSettingsEncryptor>();

    public TenantSettingTargetOverlapGateTests()
    {
        _db = new TenantConfigDbContext(new DbContextOptionsBuilder<TenantConfigDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        _encryptor.Encrypt(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        _encryptor.Decrypt(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("Shared", "Api")]
    [InlineData("Shared", "Web")]
    [InlineData("Api", "Shared")]
    public async Task Writer_ShouldReject_WhenCategoryAlreadyExistsUnderOverlappingTarget(string existingTarget, string newTarget)
    {
        await SeedSourceAsync(("InternalServiceAuth", existingTarget));

        var ex = await Assert.ThrowsAsync<TenantSettingTargetConflictException>(() =>
            CreateWriter().UpsertSettingAsync(SourceId, "InternalServiceAuth", newTarget, InternalAuthJson, true));

        Assert.Contains(existingTarget, ex.Message);
        Assert.Single(_db.TenantSettings.Where(s => s.Category == "InternalServiceAuth"));
    }

    [Fact]
    public async Task Writer_ShouldAllow_ApiAndWebSideBySide()
    {
        await SeedSourceAsync(("InternalServiceAuth", "Api"));

        var result = await CreateWriter().UpsertSettingAsync(SourceId, "InternalServiceAuth", "Web", InternalAuthJson, true);

        Assert.True(result.WasCreated);
    }

    [Fact]
    public async Task Writer_ShouldStillUpdate_ExistingRowOfAnAlreadyOverlappingTenant()
    {
        await SeedSourceAsync(("InternalServiceAuth", "Shared"), ("InternalServiceAuth", "Api"));

        var result = await CreateWriter().UpsertSettingAsync(SourceId, "InternalServiceAuth", "Api", InternalAuthJson, true);

        Assert.False(result.WasCreated);
    }

    [Fact]
    public async Task Duplicate_ShouldKeepSharedOnly_WhenSourceStoresInternalAuthAndTestAuthUnderShared()
    {
        await SeedSourceAsync(("InternalServiceAuth", "Shared"), ("TestAuthentication", "Shared"));

        await DuplicateAsync();

        var settings = await NewTenantSettingsAsync();
        Assert.Empty(TenantSettingTargetOverlap.FindOverlaps(settings.Select(s => (s.Category, s.Target))));

        var internalAuth = Assert.Single(settings, s => s.Category == "InternalServiceAuth");
        Assert.Equal("Shared", internalAuth.Target);
        Assert.Contains("new-internal-secret-key-32chars!!", internalAuth.Settings);

        var testAuth = Assert.Single(settings, s => s.Category == "TestAuthentication");
        Assert.Equal("Shared", testAuth.Target);
        Assert.Contains(NewId.ToString(), testAuth.Settings);
    }

    [Fact]
    public async Task Duplicate_ShouldWriteApiAndWeb_WhenSourceStoresInternalAuthPerApp()
    {
        await SeedSourceAsync(("InternalServiceAuth", "Api"));

        await DuplicateAsync();

        var targets = (await NewTenantSettingsAsync())
            .Where(s => s.Category == "InternalServiceAuth")
            .Select(s => s.Target)
            .OrderBy(t => t);
        Assert.Equal(new[] { "Api", "Web" }, targets);
    }

    [Fact]
    public async Task Duplicate_ShouldReject_WhenSourceAlreadyHasOverlappingRows()
    {
        await SeedSourceAsync(("Layout", "Shared"), ("Layout", "Web"));

        var ex = await Assert.ThrowsAsync<TenantSettingTargetConflictException>(DuplicateAsync);

        Assert.Contains("Layout", ex.Message);
        Assert.False(await _db.Tenants.AnyAsync(t => t.Id == NewId));
    }

    private TenantSettingsWriterService CreateWriter() =>
        new(_db, _encryptor, Substitute.For<ILogger<TenantSettingsWriterService>>());

    private Task DuplicateAsync() =>
        new TenantDuplicatorService(_db, _encryptor, Substitute.For<ILogger<TenantDuplicatorService>>())
            .DuplicateAsync(
                SourceId,
                NewId,
                "Sentinel",
                "sentinel.example.com",
                "https://sentinel.example.com",
                "new-authorization-secret-key-32ch",
                "new-internal-secret-key-32chars!!",
                [],
                "Sentinel service");

    private async Task SeedSourceAsync(params (string Category, string Target)[] settings)
    {
        var tenant = new TenantEntity { Id = SourceId, Name = "Source" };
        foreach (var (category, target) in settings)
        {
            tenant.Settings.Add(new TenantSettingEntity
            {
                Id = Guid.NewGuid(),
                TenantId = SourceId,
                Category = category,
                Target = target,
                Settings = category switch
                {
                    "InternalServiceAuth" => InternalAuthJson,
                    "TestAuthentication" => TestAuthJson,
                    _ => "{}"
                },
                IsSecret = TenantSettingsSecretCategories.ShouldEncrypt(category)
            });
        }

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private Task<List<TenantSettingEntity>> NewTenantSettingsAsync() =>
        _db.TenantSettings.AsNoTracking().Where(s => s.TenantId == NewId).ToListAsync();
}
