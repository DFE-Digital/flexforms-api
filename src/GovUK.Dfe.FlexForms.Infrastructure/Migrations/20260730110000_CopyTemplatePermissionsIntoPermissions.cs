using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations;

/// <summary>
/// Copies ea.TemplatePermissions into ea.Permissions as ResourceType.Template grants.
/// Does not drop TemplatePermissions — that will be decided later.
/// </summary>
[DbContext(typeof(ExternalApplicationsContext))]
[Migration("20260730110000_CopyTemplatePermissionsIntoPermissions")]
public partial class CopyTemplatePermissionsIntoPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ResourceType.Template = 2. SELECT from TemplatePermissions returns current temporal rows.
        migrationBuilder.Sql("""
            INSERT INTO ea.Permissions (
                PermissionId,
                UserId,
                ApplicationId,
                ResourceKey,
                ResourceType,
                AccessType,
                GrantedOn,
                GrantedBy)
            SELECT
                NEWID(),
                tp.UserId,
                NULL,
                CONVERT(nvarchar(450), tp.TemplateId),
                CAST(2 AS tinyint),
                tp.AccessType,
                tp.GrantedOn,
                tp.GrantedBy
            FROM ea.TemplatePermissions AS tp
            WHERE NOT EXISTS (
                SELECT 1
                FROM ea.Permissions AS p
                WHERE p.UserId = tp.UserId
                  AND p.ResourceType = 2
                  AND p.ResourceKey = CONVERT(nvarchar(450), tp.TemplateId)
                  AND p.AccessType = tp.AccessType
                  AND p.ApplicationId IS NULL);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty: do not delete Permissions rows that may have been
        // created by the application after this migration ran.
    }
}
