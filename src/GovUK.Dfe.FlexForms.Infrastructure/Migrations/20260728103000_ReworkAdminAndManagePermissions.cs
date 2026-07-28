using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations;

[DbContext(typeof(ExternalApplicationsContext))]
[Migration("20260728103000_ReworkAdminAndManagePermissions")]
public partial class ReworkAdminAndManagePermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Convert old Template:Manage:Write grants to *:Any:Manage.
            -- AccessType: Write=1, Manage=3.
            UPDATE ea.RolePermissions
            SET ResourceKey = N'Any',
                AccessType = 3
            WHERE ResourceKey = N'Manage'
              AND AccessType = 1
              AND ResourceType IN (2, 1);

            UPDATE ea.Permissions
            SET ResourceKey = N'Any',
                AccessType = 3
            WHERE ResourceKey = N'Manage'
              AND AccessType = 1
              AND ResourceType IN (2, 1);

            -- Tenant-scoped SuperAdmin becomes Admin; global SuperAdmin stays unchanged.
            UPDATE ea.Roles
            SET Name = N'Admin'
            WHERE TenantId IS NOT NULL
              AND Name = N'SuperAdmin';

            -- Ensure every tenant with tenant-scoped roles/memberships/templates has a system Admin role.
            ;WITH TenantIds AS (
                SELECT DISTINCT TenantId FROM ea.Roles WHERE TenantId IS NOT NULL
                UNION
                SELECT DISTINCT TenantId FROM ea.Templates WHERE TenantId IS NOT NULL
                UNION
                SELECT DISTINCT TenantId FROM ea.TenantMemberships WHERE TenantId IS NOT NULL
            )
            INSERT INTO ea.Roles (RoleId, Name, TenantId, IsSystem)
            SELECT NEWID(), N'Admin', t.TenantId, 1
            FROM TenantIds t
            WHERE NOT EXISTS (
                SELECT 1
                FROM ea.Roles r
                WHERE r.TenantId = t.TenantId
                  AND r.Name = N'Admin'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE ea.RolePermissions
            SET ResourceKey = N'Manage',
                AccessType = 1
            WHERE ResourceKey = N'Any'
              AND AccessType = 3
              AND ResourceType IN (2, 1);

            UPDATE ea.Permissions
            SET ResourceKey = N'Manage',
                AccessType = 1
            WHERE ResourceKey = N'Any'
              AND AccessType = 3
              AND ResourceType IN (2, 1);

            UPDATE ea.Roles
            SET Name = N'SuperAdmin'
            WHERE TenantId IS NOT NULL
              AND Name = N'Admin';
            """);
    }
}
