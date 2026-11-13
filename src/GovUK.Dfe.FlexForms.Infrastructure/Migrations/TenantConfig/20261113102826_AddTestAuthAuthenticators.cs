using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations.TenantConfig
{
    /// <inheritdoc />
    public partial class AddTestAuthAuthenticators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestAuthAuthenticators",
                schema: "tenantconfig",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EncryptedSecret = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAuthAuthenticators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestAuthAuthenticators_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenantconfig",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestAuthAuthenticators_TenantId_NormalizedEmail",
                schema: "tenantconfig",
                table: "TestAuthAuthenticators",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestAuthAuthenticators",
                schema: "tenantconfig");
        }
    }
}
