using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations.TenantConfig
{
    /// <inheritdoc />
    public partial class AddTestAuthAuthenticatorSetupId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SetupId",
                schema: "tenantconfig",
                table: "TestAuthAuthenticators",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupId",
                schema: "tenantconfig",
                table: "TestAuthAuthenticators");
        }
    }
}
