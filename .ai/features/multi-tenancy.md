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

Host-config is `GlobalConfiguration` flattened (prefix removed) plus allow-listed `ConnectionStrings:Redis`, minus secret-looking keys. Web merges it **over** its own settings, so anything exported wins over Web's appsettings/env. For `target=Web`, `HostConfigurationReader` drops `ApplicationInsights:ConnectionString` and `ApplicationInsights:InstrumentationKey` so Web keeps its own App Insights resource (**Verified**, `Infrastructure/Services/HostConfigurationReader.cs`). Telemetry precedence per request: tenant `ApplicationInsights` setting → that app's host connection string.

## 4. Entities

TenantConfig tables: Tenants, TenantSettings, TenantHostnames, TenantFrontendOrigins, TenantPrincipals. EA: `Template.TenantId`, `TenantMembership`, `Role.TenantId`.

## 5. Other repos

Web: hostname-first resolver, session binding. Prism: `X-Tenant-ID` on internal reads; `Prism:ExcludedTenantIds` for test tenants.

## 6. Rules

See [`../business-rules.md`](../business-rules.md). SuperAdmin-only categories. Secret redaction. HostMappings ownership.

## 7. Edge cases

- Refresh flatten failure blocks **all** tenants’ updates.
- A new tenant needs a minimum set of settings before anyone can sign in (`AzureAd` and Web token refresh included, whatever the scheme). See [`tenant-admin.md`](tenant-admin.md#minimum-settings-to-run-a-tenant-verified).
- HostMappings must only reference templates owned by the tenant (or legacy `TenantId == null`).
- `GET /v1/internal/prism/tenants` is the Prism exception to “tenant required”.

## 8. Tests

`TenantResolutionMiddlewareTests`, TenantAdmin/TenantConfig handler tests, `TenantSettingTargetOverlapGateTests`.

## 9. Limits

Changes made through the admin endpoints reach every API replica straight away (`ReplicatedTenantConfigurationProvider` broadcasts the refresh over Redis); SQL edits and missed broadcasts converge on the ~60s timer. Web keeps its own per-replica cache; changes made through the Web admin pages are broadcast there too, but writes made straight to the API wait for Web's cache to expire (`PlatformBootstrap:TenantConfigurationCacheMinutes`). See `.ai/known-pitfalls.md` item 34. Operator docs: `docs/multi-tenancy-config.md`.
