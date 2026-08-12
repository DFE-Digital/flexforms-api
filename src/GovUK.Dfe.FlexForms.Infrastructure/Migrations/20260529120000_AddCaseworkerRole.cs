using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseworkerRole : Migration
    {
        // Historical seed id; Caseworker is no longer a system role (see RemoveCaseworkerSystemRole).
        private static readonly Guid CaseworkerRoleId = Guid.Parse("C4E5F6A7-B8C9-4D0E-9F1A-2B3C4D5E6F70");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "ea",
                table: "Roles",
                columns: new[] { "RoleId", "Name" },
                values: new object[] { CaseworkerRoleId, "Caseworker" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "ea",
                table: "Roles",
                keyColumn: "RoleId",
                keyValue: CaseworkerRoleId);
        }
    }
}
