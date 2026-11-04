# Feature: Multi-tenancy

## 1. Purpose

Serve many GOV.UK products from one API with isolated config, auth, and (optionally) data.

## 2. Entry points

- `Api/Middleware/TenantResolutionMiddleware.cs`
- `Infrastructure/Services/DatabaseTenantConfigurationProvider.cs`
- `Api/Controllers/TenantConfigController.cs`, `HostConfigController.cs`, `TenantAdminController.cs`
- Domain: `ITenantContextAccessor`, `Domain/Tenancy/`

## 3. Flow

Request → header `X-Tenant-ID` or Origin map → catalogue lookup → scoped tenant + EA connection string. JWT `tenant_id` must match.

Web bootstrap: app-only token → `GET /v1/host-config?target=Web` → per request `GET /v1/tenant-config/resolve` then `GET /v1/tenant-config/tenants/{id}?target=Web`.

## 4. Entities

TenantConfig tables: Tenants, TenantSettings, TenantHostnames, TenantFrontendOrigins, TenantPrincipals. EA: `Template.TenantId`, `TenantMembership`, `Role.TenantId`.

## 5. Other repos

Web: hostname-first resolver, session binding. Prism: `X-Tenant-ID` on internal reads; `Prism:ExcludedTenantIds` for test tenants.

## 6. Rules

See [`../business-rules.md`](../business-rules.md). SuperAdmin-only categories. Secret redaction. HostMappings ownership.

## 7. Edge cases

- Refresh flatten failure blocks **all** tenants’ updates.
- Duplicator overlapping Shared/Api keys.
- HostMappings must only reference templates owned by the tenant (or legacy `TenantId == null`).
- `GET /v1/internal/prism/tenants` is the Prism exception to “tenant required”.

## 8. Tests

`TenantResolutionMiddlewareTests`, TenantAdmin/TenantConfig handler tests, `TenantSettingTargetOverlapGateTests`.

## 9. Limits

Hot reload is eventual (~60s). Per-replica Web cache can stay stale longer. Operator docs: `docs/multi-tenancy-config.md`.
