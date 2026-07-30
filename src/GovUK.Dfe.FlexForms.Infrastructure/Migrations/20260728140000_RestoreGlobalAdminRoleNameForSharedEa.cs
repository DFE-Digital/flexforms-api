using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations;

/// <summary>
/// Shared eatpp DB: old Multi-Tenant form engine keys off Roles.Name = 'Admin' for the
/// well-known global RoleId. FlexForms treats that RoleId as platform SuperAdmin in code
/// (RoleNames.FromRoleId / ExchangeToken) and issues a SuperAdmin claim — keep the DB name as Admin.
/// </summary>
[DbContext(typeof(ExternalApplicationsContext))]
[Migration("20260728140000_RestoreGlobalAdminRoleNameForSharedEa")]
public partial class RestoreGlobalAdminRoleNameForSharedEa : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Only the global well-known platform role. Do not touch tenant-scoped Roles.
            UPDATE ea.Roles
            SET Name = N'Admin'
            WHERE TenantId IS NULL
              AND RoleId = 'B32B38CA-B90B-4DBF-A788-B4280F0641EF'
              AND Name = N'SuperAdmin';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE ea.Roles
            SET Name = N'SuperAdmin'
            WHERE TenantId IS NULL
              AND RoleId = 'B32B38CA-B90B-4DBF-A788-B4280F0641EF'
              AND Name = N'Admin';
            """);
    }
}
