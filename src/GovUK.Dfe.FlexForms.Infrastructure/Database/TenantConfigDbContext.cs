using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Infrastructure.Database;

/// <summary>
/// DbContext for the tenant configuration database.
/// Separate from ExternalApplicationsContext -- this stores tenant metadata and settings,
/// not application data. Read-heavy, write-rare.
/// </summary>
public class TenantConfigDbContext(DbContextOptions<TenantConfigDbContext> options) : DbContext(options)
{
    private const string Schema = "tenantconfig";

    public DbSet<TenantEntity> Tenants { get; set; } = null!;
    public DbSet<TenantSettingEntity> TenantSettings { get; set; } = null!;
    public DbSet<TenantHostnameEntity> TenantHostnames { get; set; } = null!;
    public DbSet<TenantFrontendOriginEntity> TenantFrontendOrigins { get; set; } = null!;
    public DbSet<TenantPrincipalEntity> TenantPrincipals { get; set; } = null!;
    public DbSet<TenantSettingAuditEntity> TenantSettingAudits { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        ConfigureTenantEntity(modelBuilder);
        ConfigureTenantSettingEntity(modelBuilder);
        ConfigureTenantHostnameEntity(modelBuilder);
        ConfigureTenantFrontendOriginEntity(modelBuilder);
        ConfigureTenantPrincipalEntity(modelBuilder);
        ConfigureTenantSettingAuditEntity(modelBuilder);
    }

    private static void ConfigureTenantEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantEntity>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.Name).IsUnique();

            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasMany(e => e.Settings)
                .WithOne(s => s.Tenant)
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Hostnames)
                .WithOne(h => h.Tenant)
                .HasForeignKey(h => h.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.FrontendOrigins)
                .WithOne(o => o.Tenant)
                .HasForeignKey(o => o.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Principals)
                .WithOne(p => p.Tenant)
                .HasForeignKey(p => p.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTenantSettingEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantSettingEntity>(entity =>
        {
            entity.ToTable("TenantSettings", Schema, tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("PeriodStart");
                ttb.HasPeriodEnd("PeriodEnd");
                ttb.UseHistoryTable("History_TenantSettings", Schema);
            }));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.Category)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Target)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.Settings)
                .IsRequired();

            entity.Property(e => e.IsSecret)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.Category, e.Target }).IsUnique();
        });
    }

    private static void ConfigureTenantHostnameEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantHostnameEntity>(entity =>
        {
            entity.ToTable("TenantHostnames");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.Hostname)
                .IsRequired()
                .HasMaxLength(255);

            entity.HasIndex(e => e.Hostname).IsUnique();
        });
    }

    private static void ConfigureTenantFrontendOriginEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantFrontendOriginEntity>(entity =>
        {
            entity.ToTable("TenantFrontendOrigins");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.Origin)
                .IsRequired()
                .HasMaxLength(500);

            entity.HasIndex(e => e.Origin).IsUnique();
        });
    }

    private static void ConfigureTenantPrincipalEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantPrincipalEntity>(entity =>
        {
            entity.ToTable("TenantPrincipals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.PrincipalObjectId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.PrincipalType)
                .IsRequired()
                .HasMaxLength(40);

            entity.Property(e => e.DisplayName)
                .HasMaxLength(200);

            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasIndex(e => e.PrincipalObjectId).IsUnique();
            entity.HasIndex(e => e.TenantId);
        });
    }

    private static void ConfigureTenantSettingAuditEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantSettingAuditEntity>(entity =>
        {
            entity.ToTable("TenantSettingAudits");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.Category)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Target)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.ActorEmail)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.ChangedAtUtc).IsRequired();
            entity.Property(e => e.WasSecret).IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.ChangedAtUtc });

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
