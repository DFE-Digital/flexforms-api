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
    public async Task Create_ShouldReject_WhenNewTenantRowsOverlap()
    {
        var creator = new TenantCreatorService(_db, _encryptor, Substitute.For<ILogger<TenantCreatorService>>());

        var ex = await Assert.ThrowsAsync<TenantSettingTargetConflictException>(() => creator.CreateAsync(
            new NewTenantDefinition(
                NewId,
                "New tenant",
                "new.example.com",
                "https://new.example.com",
                [
                    new NewTenantSetting("TestAuthentication", "Shared", TestAuthJson, true),
                    new NewTenantSetting("TestAuthentication", "Web", TestAuthJson, true)
                ])));

        Assert.Contains("TestAuthentication", ex.Message);
        Assert.False(await _db.Tenants.AnyAsync(t => t.Id == NewId));
    }

    private TenantSettingsWriterService CreateWriter() =>
        new(_db, _encryptor, Substitute.For<ILogger<TenantSettingsWriterService>>());

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
}
