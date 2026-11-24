using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantHostnameResolverTests : IDisposable
{
    private static readonly Guid Transfers = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Visits = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Closed = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly TenantConfigDbContext _db = new(new DbContextOptionsBuilder<TenantConfigDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public TenantHostnameResolverTests()
    {
        _db.Tenants.AddRange(
            new TenantEntity { Id = Transfers, Name = "Transfers" },
            new TenantEntity { Id = Visits, Name = "Visits" },
            new TenantEntity { Id = Closed, Name = "Closed", IsActive = false });
        _db.TenantHostnames.AddRange(
            Hostname(Transfers, "transfers.example.gov.uk"),
            Hostname(Transfers, "apply-transfers.example.gov.uk"),
            Hostname(Visits, "visits.example.gov.uk"),
            Hostname(Closed, "closed.example.gov.uk"));
        _db.SaveChanges();
    }

    [Fact]
    public async Task Resolve_MappedHostnameOfAnActiveTenant()
    {
        var resolution = await new TenantHostnameResolver(_db).ResolveAsync("  visits.example.gov.uk ");

        Assert.NotNull(resolution);
        Assert.Equal(Visits, resolution.TenantId);
        Assert.Equal("Visits", resolution.TenantName);
        Assert.Equal("visits.example.gov.uk", resolution.Hostname);
    }

    [Theory]
    [InlineData("closed.example.gov.uk")]
    [InlineData("unknown.example.gov.uk")]
    [InlineData("example.gov.uk")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolve_InactiveUnknownOrBlankHostnameIsNull(string hostname)
    {
        Assert.Null(await new TenantHostnameResolver(_db).ResolveAsync(hostname));
    }

    [Fact]
    public async Task List_OnlyThatTenantsHostnamesInOrder()
    {
        var hostnames = await new TenantHostnameResolver(_db).ListHostnamesForTenantAsync(Transfers);

        Assert.Equal(["apply-transfers.example.gov.uk", "transfers.example.gov.uk"], hostnames);
    }

    [Fact]
    public async Task List_TenantWithNoHostnamesIsEmpty()
    {
        Assert.Empty(await new TenantHostnameResolver(_db).ListHostnamesForTenantAsync(Guid.NewGuid()));
    }

    public void Dispose() => _db.Dispose();

    private static TenantHostnameEntity Hostname(Guid tenantId, string hostname) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, Hostname = hostname };
}
