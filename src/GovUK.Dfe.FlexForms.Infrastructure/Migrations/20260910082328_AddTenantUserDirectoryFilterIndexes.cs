using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantUserDirectoryFilterIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_Name",
                schema: "ea",
                table: "Users",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_TenantId_IsActive_RoleId",
                schema: "ea",
                table: "TenantMemberships",
                columns: new[] { "TenantId", "IsActive", "RoleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Name",
                schema: "ea",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TenantMemberships_TenantId_IsActive_RoleId",
                schema: "ea",
                table: "TenantMemberships");
        }
    }
}
