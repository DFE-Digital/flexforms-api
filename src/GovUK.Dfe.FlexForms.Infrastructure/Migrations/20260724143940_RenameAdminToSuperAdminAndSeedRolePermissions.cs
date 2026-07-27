using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameAdminToSuperAdminAndSeedRolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename privileged role Admin/Administrator → SuperAdmin (global + tenant-scoped).
            migrationBuilder.Sql("""
                UPDATE ea.Roles
                SET Name = N'SuperAdmin'
                WHERE Name IN (N'Admin', N'Administrator');
                """);

            // Seed Caseworker tenant-wide RolePermissions (Application/Any/Read, ApplicationFiles/Any/Read).
            // ResourceType: Application=0, ApplicationFiles=7; AccessType: Read=0
            migrationBuilder.Sql("""
                INSERT INTO ea.RolePermissions (RolePermissionId, RoleId, ResourceKey, ResourceType, AccessType, CreatedOn)
                SELECT NEWID(), r.RoleId, N'Any', 0, 0, SYSUTCDATETIME()
                FROM ea.Roles r
                WHERE r.Name = N'Caseworker'
                  AND NOT EXISTS (
                      SELECT 1 FROM ea.RolePermissions rp
                      WHERE rp.RoleId = r.RoleId
                        AND rp.ResourceType = 0
                        AND rp.ResourceKey = N'Any'
                        AND rp.AccessType = 0);

                INSERT INTO ea.RolePermissions (RolePermissionId, RoleId, ResourceKey, ResourceType, AccessType, CreatedOn)
                SELECT NEWID(), r.RoleId, N'Any', 7, 0, SYSUTCDATETIME()
                FROM ea.Roles r
                WHERE r.Name = N'Caseworker'
                  AND NOT EXISTS (
                      SELECT 1 FROM ea.RolePermissions rp
                      WHERE rp.RoleId = r.RoleId
                        AND rp.ResourceType = 7
                        AND rp.ResourceKey = N'Any'
                        AND rp.AccessType = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE rp
                FROM ea.RolePermissions rp
                INNER JOIN ea.Roles r ON r.RoleId = rp.RoleId
                WHERE r.Name = N'Caseworker'
                  AND rp.ResourceKey = N'Any'
                  AND rp.AccessType = 0
                  AND rp.ResourceType IN (0, 7);

                UPDATE ea.Roles
                SET Name = N'Admin'
                WHERE Name = N'SuperAdmin';
                """);
        }
    }
}
