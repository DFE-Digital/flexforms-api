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
