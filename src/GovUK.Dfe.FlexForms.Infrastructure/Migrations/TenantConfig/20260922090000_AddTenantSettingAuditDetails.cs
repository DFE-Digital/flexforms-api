using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GovUK.Dfe.FlexForms.Infrastructure.Database;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations.TenantConfig;

[DbContext(typeof(TenantConfigDbContext))]
[Migration("20260922090000_AddTenantSettingAuditDetails")]
public partial class AddTenantSettingAuditDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Action",
            schema: "tenantconfig",
            table: "TenantSettingAudits",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(20)",
            oldMaxLength: 20);

        migrationBuilder.AddColumn<string>(
            name: "Details",
            schema: "tenantconfig",
            table: "TenantSettingAudits",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Details",
            schema: "tenantconfig",
            table: "TenantSettingAudits");

        migrationBuilder.AlterColumn<string>(
            name: "Action",
            schema: "tenantconfig",
            table: "TenantSettingAudits",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(32)",
            oldMaxLength: 32);
    }
}
