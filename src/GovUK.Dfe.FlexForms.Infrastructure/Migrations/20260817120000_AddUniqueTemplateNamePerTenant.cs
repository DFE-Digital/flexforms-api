using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations;

[DbContext(typeof(ExternalApplicationsContext))]
[Migration("20260817120000_AddUniqueTemplateNamePerTenant")]
public partial class AddUniqueTemplateNamePerTenant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Unique indexes treat trailing spaces as equal and typically use CI collation,
        // so "default" / "Default" / "default " all collide. Rename later duplicates
        // before creating the index.
        migrationBuilder.Sql("""
            UPDATE ea.Templates
            SET Name = RTRIM(Name)
            WHERE TenantId IS NOT NULL
              AND Name <> RTRIM(Name);

            DECLARE @id uniqueidentifier;
            DECLARE @tenant uniqueidentifier;
            DECLARE @base nvarchar(100);
            DECLARE @new nvarchar(100);
            DECLARE @n int;

            WHILE 1 = 1
            BEGIN
                SET @id = NULL;

                SELECT TOP (1)
                    @id = extra.TemplateId,
                    @tenant = extra.TenantId,
                    @base = RTRIM(extra.Name)
                FROM ea.Templates extra
                INNER JOIN ea.Templates keeper
                    ON keeper.TenantId = extra.TenantId
                   AND keeper.TemplateId <> extra.TemplateId
                   AND LOWER(RTRIM(keeper.Name)) = LOWER(RTRIM(extra.Name))
                   AND (
                        keeper.CreatedOn < extra.CreatedOn
                        OR (keeper.CreatedOn = extra.CreatedOn AND keeper.TemplateId < extra.TemplateId)
                   )
                WHERE extra.TenantId IS NOT NULL
                ORDER BY extra.CreatedOn DESC, extra.TemplateId DESC;

                IF @id IS NULL
                    BREAK;

                SET @n = 1;
                SET @new = LEFT(@base, 99) + N'1';

                WHILE EXISTS (
                    SELECT 1
                    FROM ea.Templates
                    WHERE TenantId = @tenant
                      AND LOWER(RTRIM(Name)) = LOWER(RTRIM(@new)))
                BEGIN
                    SET @n = @n + 1;
                    SET @new = LEFT(@base, 100 - LEN(CAST(@n AS varchar(10)))) + CAST(@n AS varchar(10));
                END

                UPDATE ea.Templates
                SET Name = @new
                WHERE TemplateId = @id;
            END
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Templates_TenantId_Name",
            schema: "ea",
            table: "Templates",
            columns: new[] { "TenantId", "Name" },
            unique: true,
            filter: "[TenantId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Templates_TenantId_Name",
            schema: "ea",
            table: "Templates");
    }
}
