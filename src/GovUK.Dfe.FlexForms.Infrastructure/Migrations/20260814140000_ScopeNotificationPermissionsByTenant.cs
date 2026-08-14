using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations;

/// <summary>
/// Notification permission grants were stored as <c>Notifications:{email}</c> with no tenant,
/// so User Manager edits on one tenant removed the same grant on every tenant.
/// Prefix existing rows as <c>{tenantId}:{email}</c> for each active membership.
/// </summary>
[DbContext(typeof(ExternalApplicationsContext))]
[Migration("20260814140000_ScopeNotificationPermissionsByTenant")]
public partial class ScopeNotificationPermissionsByTenant : Migration
{
    private const byte NotificationsResourceType = (byte)ResourceType.Notifications;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "ResourceKey",
            schema: "ea",
            table: "Permissions",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(200)",
            oldMaxLength: 200);

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
                p.UserId,
                p.ApplicationId,
                LOWER(CONVERT(nvarchar(36), m.TenantId)) + N':' + p.ResourceKey,
                p.ResourceType,
                p.AccessType,
                p.GrantedOn,
                p.GrantedBy
            FROM ea.Permissions p
            INNER JOIN ea.TenantMemberships m
                ON m.UserId = p.UserId
               AND m.IsActive = 1
            WHERE p.ResourceType = {NotificationsResourceType}
              AND NOT (
                    LEN(p.ResourceKey) > 37
                AND SUBSTRING(p.ResourceKey, 37, 1) = N':'
                AND TRY_CONVERT(uniqueidentifier, LEFT(p.ResourceKey, 36)) IS NOT NULL)
              AND NOT EXISTS (
                    SELECT 1
                    FROM ea.Permissions existing
                    WHERE existing.UserId = p.UserId
                      AND existing.ResourceType = p.ResourceType
                      AND existing.AccessType = p.AccessType
                      AND existing.ResourceKey = LOWER(CONVERT(nvarchar(36), m.TenantId)) + N':' + p.ResourceKey);

            DELETE FROM ea.Permissions
            WHERE ResourceType = {NotificationsResourceType}
              AND NOT (
                    LEN(ResourceKey) > 37
                AND SUBSTRING(ResourceKey, 37, 1) = N':'
                AND TRY_CONVERT(uniqueidentifier, LEFT(ResourceKey, 36)) IS NOT NULL);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
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
                p.UserId,
                p.ApplicationId,
                SUBSTRING(p.ResourceKey, 38, 256),
                p.ResourceType,
                p.AccessType,
                p.GrantedOn,
                p.GrantedBy
            FROM ea.Permissions p
            WHERE p.ResourceType = {NotificationsResourceType}
              AND LEN(p.ResourceKey) > 37
              AND SUBSTRING(p.ResourceKey, 37, 1) = N':'
              AND TRY_CONVERT(uniqueidentifier, LEFT(p.ResourceKey, 36)) IS NOT NULL
              AND NOT EXISTS (
                    SELECT 1
                    FROM ea.Permissions existing
                    WHERE existing.UserId = p.UserId
                      AND existing.ResourceType = p.ResourceType
                      AND existing.AccessType = p.AccessType
                      AND existing.ResourceKey = SUBSTRING(p.ResourceKey, 38, 256));

            DELETE FROM ea.Permissions
            WHERE ResourceType = {NotificationsResourceType}
              AND LEN(ResourceKey) > 37
              AND SUBSTRING(ResourceKey, 37, 1) = N':'
              AND TRY_CONVERT(uniqueidentifier, LEFT(ResourceKey, 36)) IS NOT NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "ResourceKey",
            schema: "ea",
            table: "Permissions",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(256)",
            oldMaxLength: 256);
    }
}
