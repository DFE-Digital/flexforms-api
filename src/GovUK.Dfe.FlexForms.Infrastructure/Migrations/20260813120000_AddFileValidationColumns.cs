using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GovUK.Dfe.FlexForms.Infrastructure.Database;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations;

[DbContext(typeof(ExternalApplicationsContext))]
[Migration("20260813120000_AddFileValidationColumns")]
public partial class AddFileValidationColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ValidationStatus",
            schema: "ea",
            table: "Files",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "NotRequired");

        migrationBuilder.AddColumn<string>(
            name: "ValidationMessage",
            schema: "ea",
            table: "Files",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ValidatedOn",
            schema: "ea",
            table: "Files",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ValidationSource",
            schema: "ea",
            table: "Files",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ValidationStatus", schema: "ea", table: "Files");
        migrationBuilder.DropColumn(name: "ValidationMessage", schema: "ea", table: "Files");
        migrationBuilder.DropColumn(name: "ValidatedOn", schema: "ea", table: "Files");
        migrationBuilder.DropColumn(name: "ValidationSource", schema: "ea", table: "Files");
    }
}
