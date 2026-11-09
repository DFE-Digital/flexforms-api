using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantCreatorServiceTests : IDisposable
{
    private static readonly Guid NewId = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private readonly TenantConfigDbContext _db;
    private readonly ITenantSettingsEncryptor _encryptor = Substitute.For<ITenantSettingsEncryptor>();
    private readonly TenantCreatorService _creator;

    public TenantCreatorServiceTests()
    {
        _db = new TenantConfigDbContext(new DbContextOptionsBuilder<TenantConfigDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _encryptor.Encrypt(Arg.Any<string>()).Returns(ci => "enc:" + ci.Arg<string>());
        _creator = new TenantCreatorService(_db, _encryptor, Substitute.For<ILogger<TenantCreatorService>>());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_ShouldPersistOnlyGivenRows_EncryptingSecrets()
    {
        var result = await _creator.CreateAsync(Definition(
            "https://New.Example.com/",
            new NewTenantSetting("ConnectionStrings", "Api", """{"DefaultConnection":"x"}""", true),
            new NewTenantSetting("Authentication", "Web", """{"Scheme":"DfESignIn"}""", false)));

        Assert.Equal(2, result.SettingsCreated);
        Assert.Equal("new.example.com", result.Hostname);

        var tenant = await _db.Tenants.AsNoTracking()
            .Include(t => t.Settings)
            .Include(t => t.Hostnames)
            .Include(t => t.FrontendOrigins)
            .SingleAsync(t => t.Id == NewId);
        Assert.True(tenant.IsActive);
        Assert.Equal("new.example.com", Assert.Single(tenant.Hostnames).Hostname);
        Assert.Equal("https://new.example.com", Assert.Single(tenant.FrontendOrigins).Origin);
        Assert.Equal("""enc:{"DefaultConnection":"x"}""", tenant.Settings.Single(s => s.Category == "ConnectionStrings").Settings);
        Assert.Equal("""{"Scheme":"DfESignIn"}""", tenant.Settings.Single(s => s.Category == "Authentication").Settings);
    }

    [Fact]
    public async Task CreateAsync_ShouldReject_WhenHostnameIsAlreadyAssigned()
    {
        var existing = new TenantEntity { Id = Guid.NewGuid(), Name = "Existing" };
        existing.Hostnames.Add(new TenantHostnameEntity { Id = Guid.NewGuid(), TenantId = existing.Id, Hostname = "new.example.com" });
        _db.Tenants.Add(existing);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _creator.CreateAsync(Definition(
            "new.example.com",
            new NewTenantSetting("Authentication", "Web", """{"Scheme":"DfESignIn"}""", false))));

        Assert.Contains("already assigned", ex.Message);
    }

    private static NewTenantDefinition Definition(string hostname, params NewTenantSetting[] settings) =>
        new(NewId, "New tenant", hostname, "https://new.example.com/", settings);
}
