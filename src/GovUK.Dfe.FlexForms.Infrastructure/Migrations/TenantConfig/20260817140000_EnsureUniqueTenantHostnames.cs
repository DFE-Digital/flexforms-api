using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations.TenantConfig;

[DbContext(typeof(TenantConfigDbContext))]
[Migration("20260817140000_EnsureUniqueTenantHostnames")]
public partial class EnsureUniqueTenantHostnames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE tenantconfig.TenantHostnames
            SET Hostname = LOWER(LTRIM(RTRIM(Hostname)))
            WHERE Hostname <> LOWER(LTRIM(RTRIM(Hostname)));

            DECLARE @id uniqueidentifier;
            DECLARE @hostname nvarchar(255);
            DECLARE @new nvarchar(255);
            DECLARE @n int;

            WHILE 1 = 1
            BEGIN
                SET @id = NULL;

                SELECT TOP (1)
                    @id = extra.Id,
                    @hostname = extra.Hostname
                FROM tenantconfig.TenantHostnames extra
                INNER JOIN tenantconfig.TenantHostnames keeper
                    ON keeper.Id <> extra.Id
                   AND LOWER(LTRIM(RTRIM(keeper.Hostname))) = LOWER(LTRIM(RTRIM(extra.Hostname)))
                   AND keeper.Id < extra.Id
                ORDER BY extra.Id DESC;

                IF @id IS NULL
                    BREAK;

                SET @n = 1;
                SET @new = LEFT(@hostname, 254) + N'1';

                WHILE EXISTS (
                    SELECT 1
                    FROM tenantconfig.TenantHostnames
                    WHERE LOWER(LTRIM(RTRIM(Hostname))) = LOWER(LTRIM(RTRIM(@new))))
                BEGIN
                    SET @n = @n + 1;
                    SET @new = LEFT(@hostname, 255 - LEN(CAST(@n AS varchar(10)))) + CAST(@n AS varchar(10));
                END

                UPDATE tenantconfig.TenantHostnames
                SET Hostname = @new
                WHERE Id = @id;
            END

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic
                    ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                INNER JOIN sys.columns c
                    ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID(N'tenantconfig.TenantHostnames')
                  AND i.is_unique = 1
                  AND i.has_filter = 0
                  AND c.name = N'Hostname')
            BEGIN
                CREATE UNIQUE INDEX [UX_TenantHostnames_Hostname]
                    ON [tenantconfig].[TenantHostnames] ([Hostname]);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'UX_TenantHostnames_Hostname'
                  AND object_id = OBJECT_ID(N'tenantconfig.TenantHostnames'))
            BEGIN
                DROP INDEX [UX_TenantHostnames_Hostname] ON [tenantconfig].[TenantHostnames];
            END
            """);
    }
}
