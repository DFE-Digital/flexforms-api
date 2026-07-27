using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure.Database.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Infrastructure.Database;

public class ExternalApplicationsContext : DbContext
{
    private readonly IConfiguration? _configuration;
    const string DefaultSchema = "ea";
    private readonly IServiceProvider _serviceProvider = null!;

    public ExternalApplicationsContext()
    {
    }

    public ExternalApplicationsContext(DbContextOptions<ExternalApplicationsContext> options, IConfiguration configuration, IServiceProvider serviceProvider)
        : base(options)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<TenantMembership> TenantMemberships { get; set; } = null!;
    public DbSet<RolePermission> RolePermissions { get; set; } = null!;
    public DbSet<Template> Templates { get; set; } = null!;
    public DbSet<TemplateVersion> TemplateVersions { get; set; } = null!;
    public DbSet<Domain.Entities.Application> Applications { get; set; } = null!;
    public DbSet<ApplicationResponse> ApplicationResponses { get; set; } = null!;
    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<TaskAssignmentLabel> TaskAssignmentLabels { get; set; } = null!;
    public DbSet<TemplatePermission> TemplatePermissions { get; set; } = null!;
    public DbSet<File> Files { get; set; } = null!;
    public DbSet<CustomApplicationStatus> CustomApplicationStatuses { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var connectionString = _configuration!.GetConnectionString("DefaultConnection");
            optionsBuilder.UseSqlServer(connectionString);
        }

        var mediator = _serviceProvider?.GetService<IMediator>();
        if (mediator != null)
        {
            optionsBuilder.AddInterceptors(new DomainEventDispatcherInterceptor(mediator));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Azure SQL: full temporal tables (system-versioned). SQLite (tests): no temporal, nullable period columns so inserts work.
        var useTemporal = Database.IsSqlServer();

        modelBuilder.Entity<Role>(b => ConfigureRole(b, useTemporal));
        modelBuilder.Entity<User>(b => ConfigureUser(b, useTemporal));
        modelBuilder.Entity<TenantMembership>(b => ConfigureTenantMembership(b, useTemporal));
        modelBuilder.Entity<RolePermission>(ConfigureRolePermission);
        modelBuilder.Entity<Template>(b => ConfigureTemplate(b, useTemporal));
        modelBuilder.Entity<TemplateVersion>(ConfigureTemplateVersion);
        modelBuilder.Entity<Domain.Entities.Application>(b => ConfigureApplication(b, useTemporal));
        modelBuilder.Entity<ApplicationResponse>(ConfigureApplicationResponse);
        modelBuilder.Entity<Permission>(ConfigurePermission);
        modelBuilder.Entity<TemplatePermission>(b => ConfigureTemplatePermission(b, useTemporal));
        modelBuilder.Entity<TaskAssignmentLabel>(ConfigureTaskAssignmentLabel);
        modelBuilder.Entity<File>(ConfigureFile);
        modelBuilder.Entity<CustomApplicationStatus>(b => ConfigureCustomApplicationStatus(b, useTemporal));

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureCustomApplicationStatus(EntityTypeBuilder<CustomApplicationStatus> b, bool useTemporal)
    {
        if (useTemporal)
            b.ToTable("CustomApplicationStatuses", DefaultSchema, tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("PeriodStart");
                ttb.HasPeriodEnd("PeriodEnd");
                ttb.UseHistoryTable("History_CustomApplicationStatuses", DefaultSchema);
            }));
        else
            b.ToTable("CustomApplicationStatuses", DefaultSchema);

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("CustomApplicationStatusId")
            .ValueGeneratedNever()
            .HasConversion(v => v.Value, v => new CustomApplicationStatusId(v))
            .IsRequired();
        b.Property(e => e.TemplateId)
            .HasColumnName("TemplateId")
            .HasConversion(v => v.Value, v => new TemplateId(v))
            .IsRequired();
        b.Property(e => e.ApplicationStatus)
            .HasColumnName("ApplicationStatus")
            .IsRequired();
        b.Property(e => e.Label)
            .HasColumnName("Label")
            .HasMaxLength(200)
            .IsRequired(false);
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();

        b.HasOne(e => e.Template)
            .WithMany()
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(e => new { e.TemplateId, e.ApplicationStatus })
            .HasDatabaseName("IX_CustomApplicationStatuses_TemplateId_ApplicationStatus")
            .IsUnique();

        if (useTemporal)
        {
            b.Property<DateTime>("PeriodStart")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            b.Property<DateTime>("PeriodEnd")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
        else
        {
            b.Property<DateTime?>("PeriodStart").HasColumnName("PeriodStart").IsRequired(false);
            b.Property<DateTime?>("PeriodEnd").HasColumnName("PeriodEnd").IsRequired(false);
        }
    }

    private static void ConfigureRole(EntityTypeBuilder<Role> b, bool useTemporal)
    {
        if (useTemporal)
            b.ToTable("Roles", DefaultSchema, tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("PeriodStart");
                ttb.HasPeriodEnd("PeriodEnd");
                ttb.UseHistoryTable("History_Roles", DefaultSchema);
            }));
        else
            b.ToTable("Roles", DefaultSchema);

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("RoleId")
            .ValueGeneratedOnAdd()
            .HasConversion(v => v.Value, v => new RoleId(v))
            .IsRequired();
        b.Property(e => e.Name)
            .HasColumnName("Name")
            .HasMaxLength(50)
            .IsRequired();
        b.Property(e => e.TenantId)
            .HasColumnName("TenantId")
            .IsRequired(false);
        b.Property(e => e.IsSystem)
            .HasColumnName("IsSystem")
            .IsRequired()
            .HasDefaultValue(false);
        // Legacy global roles keep TenantId NULL; tenant-scoped roles are unique per (TenantId, Name).
        b.HasIndex(e => new { e.TenantId, e.Name })
            .IsUnique()
            .HasDatabaseName("IX_Roles_TenantId_Name");
        b.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_Roles_TenantId");

        if (useTemporal)
        {
            b.Property<DateTime>("PeriodStart")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            b.Property<DateTime>("PeriodEnd")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
        else
        {
            b.Property<DateTime?>("PeriodStart").HasColumnName("PeriodStart").IsRequired(false);
            b.Property<DateTime?>("PeriodEnd").HasColumnName("PeriodEnd").IsRequired(false);
        }
    }

    private static void ConfigureTenantMembership(EntityTypeBuilder<TenantMembership> b, bool useTemporal)
    {
        if (useTemporal)
            b.ToTable("TenantMemberships", DefaultSchema, tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("PeriodStart");
                ttb.HasPeriodEnd("PeriodEnd");
                ttb.UseHistoryTable("History_TenantMemberships", DefaultSchema);
            }));
        else
            b.ToTable("TenantMemberships", DefaultSchema);

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("TenantMembershipId")
            .ValueGeneratedNever()
            .HasConversion(v => v.Value, v => new TenantMembershipId(v))
            .IsRequired();
        b.Property(e => e.TenantId)
            .HasColumnName("TenantId")
            .IsRequired();
        b.Property(e => e.UserId)
            .HasColumnName("UserId")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.Property(e => e.RoleId)
            .HasColumnName("RoleId")
            .HasConversion(v => v.Value, v => new RoleId(v))
            .IsRequired();
        b.Property(e => e.IsActive)
            .HasColumnName("IsActive")
            .IsRequired()
            .HasDefaultValue(true);
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.LastModifiedOn)
            .HasColumnName("LastModifiedOn")
            .IsRequired(false);

        b.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(e => new { e.TenantId, e.UserId })
            .IsUnique()
            .HasDatabaseName("IX_TenantMemberships_TenantId_UserId");
        b.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_TenantMemberships_UserId");

        if (useTemporal)
        {
            b.Property<DateTime>("PeriodStart")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            b.Property<DateTime>("PeriodEnd")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
        else
        {
            b.Property<DateTime?>("PeriodStart").HasColumnName("PeriodStart").IsRequired(false);
            b.Property<DateTime?>("PeriodEnd").HasColumnName("PeriodEnd").IsRequired(false);
        }
    }

    private static void ConfigureRolePermission(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions", DefaultSchema);

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("RolePermissionId")
            .ValueGeneratedNever()
            .HasConversion(v => v.Value, v => new RolePermissionId(v))
            .IsRequired();
        b.Property(e => e.RoleId)
            .HasColumnName("RoleId")
            .HasConversion(v => v.Value, v => new RoleId(v))
            .IsRequired();
        b.Property(e => e.ResourceKey)
            .HasColumnName("ResourceKey")
            .HasMaxLength(256)
            .IsRequired();
        b.Property(e => e.ResourceType)
            .HasColumnName("ResourceType")
            .IsRequired();
        b.Property(e => e.AccessType)
            .HasColumnName("AccessType")
            .IsRequired();
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();

        b.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(e => new { e.RoleId, e.ResourceType, e.ResourceKey, e.AccessType })
            .IsUnique()
            .HasDatabaseName("IX_RolePermissions_Role_Resource_Access");
    }

    private static void ConfigureUser(EntityTypeBuilder<User> b, bool useTemporal)
    {
        if (useTemporal)
            b.ToTable("Users", DefaultSchema, tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("PeriodStart");
                ttb.HasPeriodEnd("PeriodEnd");
                ttb.UseHistoryTable("History_Users", DefaultSchema);
            }));
        else
            b.ToTable("Users", DefaultSchema);

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("UserId")
            .ValueGeneratedOnAdd()
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.Property(e => e.RoleId)
            .HasColumnName("RoleId")
            .HasConversion(v => v.Value, v => new RoleId(v))
            .IsRequired();
        b.Property(e => e.Name)
            .HasColumnName("Name")
            .HasMaxLength(100)
            .IsRequired();
        b.Property(e => e.Email)
            .HasColumnName("Email")
            .HasMaxLength(256)
            .IsRequired();
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasConversion(v => v!.Value, v => new UserId(v))
            .IsRequired(false);
        b.Property(e => e.LastModifiedOn)
            .HasColumnName("LastModifiedOn")
            .IsRequired(false);
        b.Property(e => e.LastModifiedBy)
            .HasColumnName("LastModifiedBy")
            .HasConversion(v => v!.Value, v => new UserId(v))
            .IsRequired(false);
        b.Property(u => u.ExternalProviderId)
            .HasMaxLength(100)
            .IsUnicode(false);
        b.HasIndex(u => u.ExternalProviderId).IsUnique();
        b.HasIndex(e => e.Email).IsUnique();
        b.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId);
        b.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.LastModifiedByUser)
            .WithMany()
            .HasForeignKey(e => e.LastModifiedBy)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasMany(u => u.Permissions)
            .WithOne(p => p.User)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasMany(u => u.TemplatePermissions)
            .WithOne(p => p.User)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        if (useTemporal)
        {
            b.Property<DateTime>("PeriodStart")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            b.Property<DateTime>("PeriodEnd")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
        else
        {
            b.Property<DateTime?>("PeriodStart").HasColumnName("PeriodStart").IsRequired(false);
            b.Property<DateTime?>("PeriodEnd").HasColumnName("PeriodEnd").IsRequired(false);
        }
    }

    private static void ConfigureTemplate(EntityTypeBuilder<Template> b, bool useTemporal)
    {
        if (useTemporal)
            b.ToTable("Templates", DefaultSchema, tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("PeriodStart");
                ttb.HasPeriodEnd("PeriodEnd");
                ttb.UseHistoryTable("History_Templates", DefaultSchema);
            }));
        else
            b.ToTable("Templates", DefaultSchema);

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("TemplateId")
            .ValueGeneratedOnAdd()
            .HasConversion(v => v.Value, v => new TemplateId(v))
            .IsRequired();
        b.Property(e => e.Name)
            .HasColumnName("Name")
            .HasMaxLength(100)
            .IsRequired();
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.Property(e => e.IsLive)
            .HasColumnName("IsLive")
            .IsRequired()
            .HasDefaultValue(false);
        b.Property(e => e.TenantId)
            .HasColumnName("TenantId")
            .IsRequired(false);
        b.HasIndex(e => e.TenantId);

        b.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        if (useTemporal)
        {
            b.Property<DateTime>("PeriodStart")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            b.Property<DateTime>("PeriodEnd")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
        else
        {
            b.Property<DateTime?>("PeriodStart").HasColumnName("PeriodStart").IsRequired(false);
            b.Property<DateTime?>("PeriodEnd").HasColumnName("PeriodEnd").IsRequired(false);
        }
    }

    private static void ConfigureTemplateVersion(EntityTypeBuilder<TemplateVersion> b)
    {
        b.ToTable("TemplateVersions", DefaultSchema);
        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("TemplateVersionId")
            .ValueGeneratedNever()
            .HasConversion(v => v.Value, v => new TemplateVersionId(v))
            .IsRequired();
        b.Property(e => e.TemplateId)
            .HasColumnName("TemplateId")
            .HasConversion(v => v.Value, v => new TemplateId(v))
            .IsRequired();
        b.Property(e => e.VersionNumber)
            .HasColumnName("VersionNumber")
            .HasMaxLength(50)
            .IsRequired();
        b.Property(e => e.JsonSchema)
            .HasColumnName("JsonSchema")
            .IsRequired();
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.Property(e => e.LastModifiedOn)
            .HasColumnName("LastModifiedOn")
            .IsRequired(false);
        b.Property(e => e.LastModifiedBy)
            .HasColumnName("LastModifiedBy")
            .HasConversion(v => v!.Value, v => new UserId(v))
            .IsRequired(false);

        b.HasOne(e => e.Template)
            .WithMany(a => a.TemplateVersions)
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.NoAction);
        b.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.LastModifiedByUser)
            .WithMany()
            .HasForeignKey(e => e.LastModifiedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports: GetLatestTemplateVersionForTemplateQueryObject (WHERE TemplateId ORDER BY CreatedOn DESC)
        b.HasIndex(e => new { e.TemplateId, e.CreatedOn })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TemplateVersions_TemplateId_CreatedOn");
    }

    private static void ConfigureApplication(EntityTypeBuilder<Domain.Entities.Application> b, bool useTemporal)
    {
        if (useTemporal)
            b.ToTable("Applications", DefaultSchema, tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("PeriodStart");
                ttb.HasPeriodEnd("PeriodEnd");
                ttb.UseHistoryTable("History_Applications", DefaultSchema);
            }));
        else
            b.ToTable("Applications", DefaultSchema);

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("ApplicationId")
            .ValueGeneratedOnAdd()
            .HasConversion(v => v.Value, v => new ApplicationId(v))
            .IsRequired();
        b.Property(e => e.ApplicationReference)
            .HasColumnName("ApplicationReference")
            .HasMaxLength(20)
            .IsRequired();
        b.Property(e => e.TemplateVersionId)
            .HasColumnName("TemplateVersionId")
            .HasConversion(v => v.Value, v => new TemplateVersionId(v))
            .IsRequired();
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.Property(e => e.Status)
            .HasColumnName("Status")
            .IsRequired(false);
        b.Property(e => e.LastModifiedOn)
            .HasColumnName("LastModifiedOn")
            .IsRequired(false);
        b.Property(e => e.LastModifiedBy)
            .HasColumnName("LastModifiedBy")
            .HasConversion(v => v!.Value, v => new UserId(v))
            .IsRequired(false);

        b.HasOne(e => e.TemplateVersion)
            .WithMany()
            .HasForeignKey(e => e.TemplateVersionId);
        b.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.LastModifiedByUser)
            .WithMany()
            .HasForeignKey(e => e.LastModifiedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Index for efficient lookup by ApplicationReference (used by GET /Applications/reference/{applicationReference})
        b.HasIndex(e => e.ApplicationReference)
            .IsUnique()
            .HasDatabaseName("IX_Applications_ApplicationReference");

        // Supports: GetApplicationsByTemplateIdQueryObject (joins via TemplateVersionId)
        b.HasIndex(e => e.TemplateVersionId)
            .HasDatabaseName("IX_Applications_TemplateVersionId");

        b.HasIndex(e => e.CreatedOn)
            .HasDatabaseName("IX_Applications_CreatedOn");

        b.HasIndex(e => new { e.Status, e.LastModifiedOn })
            .HasDatabaseName("IX_Applications_Status_LastModifiedOn");

        if (useTemporal)
        {
            b.Property<DateTime>("PeriodStart")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            b.Property<DateTime>("PeriodEnd")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
        else
        {
            b.Property<DateTime?>("PeriodStart").HasColumnName("PeriodStart").IsRequired(false);
            b.Property<DateTime?>("PeriodEnd").HasColumnName("PeriodEnd").IsRequired(false);
        }
    }

    private static void ConfigureApplicationResponse(EntityTypeBuilder<ApplicationResponse> b)
    {
        b.ToTable("ApplicationResponses", DefaultSchema);
        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("ResponseId")
            .ValueGeneratedNever()
            .HasConversion(v => v.Value, v => new ResponseId(v))
            .IsRequired();
        b.Property(e => e.ApplicationId)
            .HasColumnName("ApplicationId")
            .HasConversion(v => v.Value, v => new ApplicationId(v))
            .IsRequired();
        b.Property(e => e.ResponseBody)
            .HasColumnName("ResponseBody")
            .IsRequired();
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.Property(e => e.LastModifiedOn)
            .HasColumnName("LastModifiedOn")
            .IsRequired(false);
        b.Property(e => e.LastModifiedBy)
            .HasColumnName("LastModifiedBy")
            .HasConversion(v => v!.Value, v => new UserId(v))
            .IsRequired(false);

        b.HasOne(e => e.Application)
            .WithMany(a => a.Responses)
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.LastModifiedByUser)
            .WithMany()
            .HasForeignKey(e => e.LastModifiedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports: latest response lookup (WHERE ApplicationId ORDER BY CreatedOn DESC)
        b.HasIndex(e => new { e.ApplicationId, e.CreatedOn })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ApplicationResponses_ApplicationId_CreatedOn");
    }

    private static void ConfigurePermission(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions", DefaultSchema);
        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("PermissionId")
            .ValueGeneratedNever()
            .HasConversion(v => v.Value, v => new PermissionId(v))
            .IsRequired();
        b.Property(e => e.UserId)
            .HasColumnName("UserId")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.Property(e => e.ApplicationId)
            .HasColumnName("ApplicationId")
            .HasConversion(v => v.Value, v => new ApplicationId(v));
        b.Property(e => e.ResourceType)
            .HasColumnName("ResourceType")
            .HasConversion(
                v => (byte)v,
                v => (ResourceType)v)
            .IsRequired();
        b.Property(e => e.ResourceKey)
            .HasColumnName("ResourceKey")
            .HasMaxLength(200)
            .IsRequired();
        b.Property(e => e.AccessType)
            .HasColumnName("AccessType")
            .HasConversion(
            v => (byte)v,
            v => (AccessType)v)
            .IsRequired();
        b.Property(e => e.GrantedOn)
            .HasColumnName("GrantedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.GrantedBy)
            .HasColumnName("GrantedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.HasOne(p => p.User)
            .WithMany(u => u.Permissions)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Application)
            .WithMany()
            .HasForeignKey(e => e.ApplicationId);
        b.HasOne(e => e.GrantedByUser)
            .WithMany()
            .HasForeignKey(e => e.GrantedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports: contributor lookup & permission checks by application
        b.HasIndex(e => new { e.ApplicationId, e.ResourceType })
            .HasDatabaseName("IX_Permissions_ApplicationId_ResourceType");

        // Supports: loading permissions for a user (included collections and filters)
        b.HasIndex(e => new { e.UserId, e.ResourceType, e.ApplicationId })
            .HasDatabaseName("IX_Permissions_UserId_ResourceType_ApplicationId");
    }

    private static void ConfigureTaskAssignmentLabel(EntityTypeBuilder<TaskAssignmentLabel> b)
    {
        b.ToTable("TaskAssignmentLabels", DefaultSchema);
        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("TaskAssignmentLabelsId")
            .ValueGeneratedOnAdd()
            .HasConversion(v => v.Value, v => new TaskAssignmentLabelId(v))
            .IsRequired();
        b.Property(e => e.Value)
            .HasColumnName("Value")
            .HasMaxLength(100)
            .IsRequired();
        b.Property(e => e.TaskId)
            .HasColumnName("TaskId")
            .HasMaxLength(10)
            .IsRequired();
        b.Property(e => e.UserId)
            .HasColumnName("UserId")
            .HasConversion(v => v!.Value, v => new UserId(v))
            .IsRequired(false);
        b.Property(e => e.CreatedOn)
            .HasColumnName("CreatedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();

        b.HasOne(e => e.AssignedUser)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureTemplatePermission(EntityTypeBuilder<TemplatePermission> b, bool useTemporal)
    {
        if (useTemporal)
            b.ToTable("TemplatePermissions", DefaultSchema, tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("PeriodStart");
                ttb.HasPeriodEnd("PeriodEnd");
                ttb.UseHistoryTable("History_TemplatePermissions", DefaultSchema);
            }));
        else
            b.ToTable("TemplatePermissions", DefaultSchema);

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("TemplatePermissionId")
            .ValueGeneratedNever()
            .HasConversion(v => v.Value, v => new TemplatePermissionId(v))
            .IsRequired();
        b.Property(e => e.UserId)
            .HasColumnName("UserId")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.Property(e => e.TemplateId)
            .HasColumnName("TemplateId")
            .HasConversion(v => v.Value, v => new TemplateId(v))
            .IsRequired();
        b.Property(e => e.AccessType)
            .HasColumnName("AccessType")
            .HasConversion(
                v => (byte)v,
                v => (AccessType)v)
            .IsRequired();
        b.Property(e => e.GrantedOn)
            .HasColumnName("GrantedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.GrantedBy)
            .HasColumnName("GrantedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.HasOne(e => e.Template)
            .WithMany()
            .HasForeignKey(e => e.TemplateId);
        b.HasOne(e => e.GrantedByUser)
            .WithMany()
            .HasForeignKey(e => e.GrantedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports: template permission lookups by (UserId, TemplateId)
        b.HasIndex(e => new { e.UserId, e.TemplateId })
            .HasDatabaseName("IX_TemplatePermissions_UserId_TemplateId");

        if (useTemporal)
        {
            b.Property<DateTime>("PeriodStart")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            b.Property<DateTime>("PeriodEnd")
                .ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
        else
        {
            b.Property<DateTime?>("PeriodStart").HasColumnName("PeriodStart").IsRequired(false);
            b.Property<DateTime?>("PeriodEnd").HasColumnName("PeriodEnd").IsRequired(false);
        }
    }

    private static void ConfigureFile(EntityTypeBuilder<File> b)
    {
        b.ToTable("Files", DefaultSchema);
        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasColumnName("FileId")
            .ValueGeneratedOnAdd()
            .HasConversion(v => v.Value, v => new FileId(v))
            .IsRequired();
        b.Ignore(f => f.IsDeleted);
        b.Property(e => e.ApplicationId)
            .HasColumnName("ApplicationId")
            .HasConversion(v => v.Value, v => new ApplicationId(v))
            .IsRequired();
        b.Property(e => e.Name)
            .HasColumnName("Name")
            .HasMaxLength(255)
            .IsRequired();
        b.Property(e => e.Description)
            .HasColumnName("Description")
            .HasMaxLength(1000)
            .IsRequired(false);
        b.Property(e => e.OriginalFileName)
            .HasColumnName("OriginalFileName")
            .HasMaxLength(255)
            .IsRequired();
        b.Property(e => e.FileName)
            .HasColumnName("FileName")
            .HasMaxLength(255)
            .IsRequired();
        b.Property(e => e.FileSize)
            .HasColumnName("FileSize")
            .IsRequired();
        b.Property(e => e.Path)
            .HasColumnName("Path")
            .HasMaxLength(255);
        b.Property(e => e.UploadedOn)
            .HasColumnName("UploadedOn")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        b.Property(e => e.UploadedBy)
            .HasColumnName("UploadedBy")
            .HasConversion(v => v.Value, v => new UserId(v))
            .IsRequired();
        b.HasOne(e => e.Application)
            .WithMany(a => a.Files)
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.UploadedByUser)
            .WithMany(a => a.Files)
            .HasForeignKey(e => e.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports: GetFilesByApplicationIdQueryObject
        b.HasIndex(e => e.ApplicationId)
            .HasDatabaseName("IX_Files_ApplicationId");

        // Supports: GetFileByFileNameApplicationIdQueryObject
        b.HasIndex(e => new { e.ApplicationId, e.FileName })
            .HasDatabaseName("IX_Files_ApplicationId_FileName");

        // Supports: GetFileByPathAndFileNameQueryObject (file share / virus scan callback)
        b.HasIndex(e => new { e.Path, e.FileName })
            .HasDatabaseName("IX_Files_Path_FileName");
    }

}
