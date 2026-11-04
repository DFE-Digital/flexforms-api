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
| TemplateManager | `Template:Any:Write`, `Template:Any:Manage` |

**Historical:** TemplateManager originally granted only `Template:Any:Manage`, so template managers could not start applications on other people's forms. `Template:Any:Write` was added; roles created before that need it added by hand.

## 7. Edge cases

Listing applications by many template GUIDs historically broke EF `Contains` on value objects — use query objects that EF can translate.

## 8. Tests

Handler tests, `ExpectedSecurity.json`, `TemplatePermissionHandlerTests` (policy handler name is historical).

## 9. Limits

Claims on the JWT can be stale until refresh. Web permission cache adds another layer.
