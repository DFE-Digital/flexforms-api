using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations;

/// <summary>
/// Memberships added after <c>ScopeNotificationPermissionsByTenant</c> for users who already existed
/// (self-registration on a second tenant, role assignment) got no <c>{tenantId}:{email}</c> notification
/// grants, so every page load returned 403 from <c>/v1/Notifications/unread</c>.
/// Grants Read/Write/Delete only where the membership was created after that migration and has no
/// notification grant at all for its tenant. Members with any grant (e.g. Read only, set in User
/// Manager) are left as configured.
/// </summary>
[DbContext(typeof(ExternalApplicationsContext))]
[Migration("20261106100000_BackfillTenantNotificationPermissions")]
public partial class BackfillTenantNotificationPermissions : Migration
{
    private const byte NotificationsResourceType = (byte)ResourceType.Notifications;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
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
                u.UserId,
                NULL,
                LOWER(CONVERT(nvarchar(36), m.TenantId)) + N':' + u.Email,
                {NotificationsResourceType},
                a.AccessType,
                GETUTCDATE(),
                u.UserId
            FROM ea.TenantMemberships m
            INNER JOIN ea.Users u
                ON u.UserId = m.UserId
            CROSS JOIN (VALUES
                (CAST({(byte)AccessType.Read} AS tinyint)),
                (CAST({(byte)AccessType.Write} AS tinyint)),
                (CAST({(byte)AccessType.Delete} AS tinyint))) a(AccessType)
            WHERE m.IsActive = 1
              AND m.CreatedOn >= '2026-08-14'
              AND u.Email IS NOT NULL
              AND LEN(u.Email) BETWEEN 1 AND 219
              AND NOT EXISTS (
                    SELECT 1
                    FROM ea.Permissions existing
                    WHERE existing.UserId = u.UserId
                      AND existing.ResourceType = {NotificationsResourceType}
                      AND existing.ResourceKey = LOWER(CONVERT(nvarchar(36), m.TenantId)) + N':' + u.Email);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data backfill only. Backfilled rows are indistinguishable from runtime grants, so nothing is removed.
    }
}
