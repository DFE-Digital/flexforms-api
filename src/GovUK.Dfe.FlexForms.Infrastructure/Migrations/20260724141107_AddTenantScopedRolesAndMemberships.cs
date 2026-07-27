using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantScopedRolesAndMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_Name",
                schema: "ea",
                table: "Roles");

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                schema: "ea",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "ea",
                table: "Roles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                schema: "ea",
                columns: table => new
                {
                    RolePermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    AccessType = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.RolePermissionId);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "ea",
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantMemberships",
                schema: "ea",
                columns: table => new
                {
                    TenantMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    LastModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMemberships", x => x.TenantMembershipId);
                    table.ForeignKey(
                        name: "FK_TenantMemberships_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "ea",
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "ea",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "History_TenantMemberships")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "ea")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId",
                schema: "ea",
                table: "Roles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_Name",
                schema: "ea",
                table: "Roles",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_Role_Resource_Access",
                schema: "ea",
                table: "RolePermissions",
                columns: new[] { "RoleId", "ResourceType", "ResourceKey", "AccessType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_RoleId",
                schema: "ea",
                table: "TenantMemberships",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_TenantId_UserId",
                schema: "ea",
                table: "TenantMemberships",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_UserId",
                schema: "ea",
                table: "TenantMemberships",
                column: "UserId");

            // Mark legacy global system roles.
            migrationBuilder.Sql("""
                UPDATE ea.Roles
                SET IsSystem = 1
                WHERE TenantId IS NULL
                  AND Name IN (N'Admin', N'Administrator', N'User', N'Caseworker');
                """);

            // Seed tenant-scoped system roles for every tenant that owns templates,
            // plus the well-known SaaS tenant ids used by FlexForms.
            migrationBuilder.Sql("""
                ;WITH TenantIds AS (
                    SELECT DISTINCT TenantId
                    FROM ea.Templates
                    WHERE TenantId IS NOT NULL
                    UNION
                    SELECT CAST('11111111-1111-4111-8111-111111111111' AS uniqueidentifier)
                    UNION
                    SELECT CAST('22222222-2222-4222-8222-222222222222' AS uniqueidentifier)
                    UNION
                    SELECT CAST('33333333-3333-4333-8333-333333333333' AS uniqueidentifier)
                ),
                SystemRoleNames AS (
                    SELECT N'SuperAdmin' AS Name UNION ALL
                    SELECT N'User' UNION ALL
                    SELECT N'Caseworker'
                )
                INSERT INTO ea.Roles (RoleId, Name, TenantId, IsSystem)
                SELECT NEWID(), s.Name, t.TenantId, 1
                FROM TenantIds t
                CROSS JOIN SystemRoleNames s
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM ea.Roles r
                    WHERE r.TenantId = t.TenantId
                      AND r.Name = s.Name
                );
                """);

            // Backfill memberships from template permissions where Template.TenantId is known.
            // Role is mapped from the user's global role name (Administrator → Admin).
            migrationBuilder.Sql("""
                ;WITH Candidate AS (
                    SELECT DISTINCT
                        t.TenantId,
                        tp.UserId,
                        CASE
                            WHEN r.Name IN (N'Admin', N'Administrator', N'SuperAdmin') THEN N'SuperAdmin'
                            WHEN r.Name = N'Caseworker' THEN N'Caseworker'
                            ELSE N'User'
                        END AS RoleName
                    FROM ea.TemplatePermissions tp
                    INNER JOIN ea.Templates t ON t.TemplateId = tp.TemplateId
                    INNER JOIN ea.Users u ON u.UserId = tp.UserId
                    INNER JOIN ea.Roles r ON r.RoleId = u.RoleId
                    WHERE t.TenantId IS NOT NULL
                )
                INSERT INTO ea.TenantMemberships (TenantMembershipId, TenantId, UserId, RoleId, IsActive, CreatedOn)
                SELECT NEWID(), c.TenantId, c.UserId, tr.RoleId, 1, SYSUTCDATETIME()
                FROM Candidate c
                INNER JOIN ea.Roles tr
                    ON tr.TenantId = c.TenantId
                   AND tr.Name = c.RoleName
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM ea.TenantMemberships m
                    WHERE m.TenantId = c.TenantId
                      AND m.UserId = c.UserId
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissions",
                schema: "ea");

            migrationBuilder.DropTable(
                name: "TenantMemberships",
                schema: "ea")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "History_TenantMemberships")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "ea")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId",
                schema: "ea",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId_Name",
                schema: "ea",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                schema: "ea",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "ea",
                table: "Roles");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                schema: "ea",
                table: "Roles",
                column: "Name",
                unique: true);
        }
    }
}
