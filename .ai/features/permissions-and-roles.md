# Feature: Permissions and roles

## 1. Purpose

Platform SuperAdmin vs tenant Admin/User/custom roles, plus resource claims.

## 2. Entry points

- `Domain/Common/RoleConstants.cs`
- `Application/Services/ClaimBasedPermissionCheckerService.cs`, `RolePermissionService.cs`, `TenantMembershipService.cs`
- `Api/Security/Handlers/*`
- `UsersController`, `RolesController`

## 3. Flow

Token exchange loads membership + merged claims. Policies such as `CanManageUsers`, `CanReadTemplate`, `TenantAdminUser`, `ServiceCallers`, `PlatformPrismRead` sit on controllers.

## 4. Entities

`Role` (nullable `TenantId`), `TenantMembership`, `RolePermission`, `Permission`.

## 5. Other repos

Web `AdminAccessHelper` mirrors SuperAdmin/Admin/Manage claims for UI. Same claim strings.

## 6. Rules

Never assign `RoleConstants.AdminRoleId` as tenant Admin. Caseworker is legacy. File-validation write is a distinct grant.

`Template:*:Manage` does **not** imply `Template:*:Write` in any evaluator (API policy `CanCreateAnyApplication`, `CreateApplicationCommandHandler`, Web `ApplicationPermissionHelper.CanCreateApplicationsForTemplate`). Starting an application always needs a Template Write grant (or Admin). **Verified.**

Role presets (`Domain/Services/RoleTemplates.cs`) are copied into `RolePermissions` when the role is created (`CreateTenantRoleFromTemplateCommandHandler`). Changing a preset does not update existing roles — edit them in Role Manager.

| Preset | Grants |
|--------|--------|
| Caseworker | `Application:Any:Read`, `ApplicationFiles:Any:Read` |
| TemplateManager | `Template:Any:Manage` |

Template managers see every template (`GetAccessibleTemplatesQueryHandler` returns the full catalogue for `CanManageTemplates`) but only **work in** templates they have been given: a `Template:{id}:Read`/`Write` user grant from User Manager, or one they created (`CreateTemplateCommandHandler` grants the author Read + Write). This is intentional — the preset deliberately has no `Template:Any:Write`. Web greys out the rest (flexforms-web `.ai/features/admin-and-studio.md`).

Per-template checks for non-admins (**Verified**):

| Action | Needs |
|--------|-------|
| Start an application | Template Write (exact or `Any`) |
| Save a template version | Template Write on that template (exact; `Any` is not honoured) |
| Save a custom status label | Template Read or Write (exact or `Any`) — `UpdateCustomApplicationStatusCommandHandler` |
| Make a template live / not live | Template Read or Write (exact or `Any`) — `SetTemplateLiveCommandHandler` |
| Grant a template to all users | Template Read or Write (exact or `Any`) — `GrantTemplateAccessToAllUsersCommandHandler`. Without it a manager could grant themselves a template they were not given. |

`GET /v1/Roles` stays **Admin/SuperAdmin only**. Template Studio does not call it for template managers (Web hides the contributor-role picker instead).

## 7. Edge cases

Listing applications by many template GUIDs historically broke EF `Contains` on value objects — use query objects that EF can translate.

## 8. Tests

Handler tests, `ExpectedSecurity.json`, `TemplatePermissionHandlerTests` (policy handler name is historical).

## 9. Limits

Claims on the JWT can be stale until refresh. Web permission cache adds another layer.
