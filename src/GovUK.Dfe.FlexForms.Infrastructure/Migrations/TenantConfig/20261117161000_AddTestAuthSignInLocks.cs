using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations.TenantConfig
{
    /// <inheritdoc />
    public partial class AddTestAuthSignInLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestAuthSignInLocks",
                schema: "tenantconfig",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedByEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAuthSignInLocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestAuthSignInLocks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenantconfig",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestAuthSignInLocks_TenantId_NormalizedEmail",
                schema: "tenantconfig",
                table: "TestAuthSignInLocks",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestAuthSignInLocks",
                schema: "tenantconfig");
        }
    }
}
