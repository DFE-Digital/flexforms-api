using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations;

/// <summary>
/// Removes the legacy Caseworker system role.
/// Users/memberships on Caseworker are moved to User; custom (non-system) roles named Caseworker are kept.
/// </summary>
[DbContext(typeof(ExternalApplicationsContext))]
[Migration("20260812160000_RemoveCaseworkerSystemRole")]
public partial class RemoveCaseworkerSystemRole : Migration
{
    private static readonly Guid LegacyGlobalCaseworkerRoleId =
        Guid.Parse("C4E5F6A7-B8C9-4D0E-9F1A-2B3C4D5E6F70");

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1) Remap Users.RoleId from global Caseworker → global User
        migrationBuilder.Sql($"""
            UPDATE u
            SET u.RoleId = '{RoleConstants.UserRoleId}'
            FROM ea.Users u
            WHERE u.RoleId = '{LegacyGlobalCaseworkerRoleId}';
            """);

        // 2) Remap tenant memberships from Caseworker system roles → that tenant's User system role
        migrationBuilder.Sql("""
            UPDATE m
            SET m.RoleId = ur.RoleId
            FROM ea.TenantMemberships m
            INNER JOIN ea.Roles cr
                ON cr.RoleId = m.RoleId
               AND cr.Name = N'Caseworker'
               AND cr.IsSystem = 1
            INNER JOIN ea.Roles ur
                ON ur.TenantId = cr.TenantId
               AND ur.Name = N'User'
               AND ur.IsSystem = 1
            WHERE cr.TenantId IS NOT NULL;
            """);

        // Memberships on the legacy global Caseworker role → tenant User when TenantId is known via membership
        migrationBuilder.Sql($"""
            UPDATE m
            SET m.RoleId = ur.RoleId
            FROM ea.TenantMemberships m
            INNER JOIN ea.Roles ur
                ON ur.TenantId = m.TenantId
               AND ur.Name = N'User'
               AND ur.IsSystem = 1
            WHERE m.RoleId = '{LegacyGlobalCaseworkerRoleId}';
            """);

        // 3) Delete RolePermissions for Caseworker system roles
        migrationBuilder.Sql($"""
            DELETE rp
            FROM ea.RolePermissions rp
            INNER JOIN ea.Roles r ON r.RoleId = rp.RoleId
            WHERE (r.Name = N'Caseworker' AND r.IsSystem = 1)
               OR r.RoleId = '{LegacyGlobalCaseworkerRoleId}';
            """);

        // 4) Delete Caseworker system roles (tenant-scoped and global)
        migrationBuilder.Sql($"""
            DELETE FROM ea.Roles
            WHERE (Name = N'Caseworker' AND IsSystem = 1)
               OR RoleId = '{LegacyGlobalCaseworkerRoleId}';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Re-seed global Caseworker only; tenant copies / memberships are not restored.
        migrationBuilder.Sql($"""
            IF NOT EXISTS (SELECT 1 FROM ea.Roles WHERE RoleId = '{LegacyGlobalCaseworkerRoleId}')
            BEGIN
                INSERT INTO ea.Roles (RoleId, Name, TenantId, IsSystem)
                VALUES ('{LegacyGlobalCaseworkerRoleId}', N'Caseworker', NULL, 1);
            END
            """);
    }
}
