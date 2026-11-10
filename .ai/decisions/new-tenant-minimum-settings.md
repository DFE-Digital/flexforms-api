# New tenant writes only the minimum settings

- **Status:** Accepted
- **Provenance:** **Verified** — `CreateTenantCommandHandler`, `NewTenantSettingsRequirements`, `TenantCreatorService`; Web `NewTenantAdminService`.

## Context

"New tenant" used to clone every TenantConfig row of the SuperAdmin's tenant, then patch a few (new Authorization and InternalServiceAuth secrets, forced Test Authentication, empty template HostMappings, Layout service name). New tenants inherited settings nobody had reviewed (Layout, FileStorage, EventTriggers, feature flags, …), and the patch logic had to know about every category. Cloning overlapping Shared + Api rows also crashed the catalogue refresh (see `known-pitfalls.md` #3).

## Decision

The Web page prefills the rows a tenant needs to run (see [`../features/tenant-admin.md`](../features/tenant-admin.md#minimum-settings-to-run-a-tenant-verified)) from the current tenant. It also prefills a fixed list of optional categories that are usually configured before go-live (`OptionalCategories`), copied as-is and marked as not required. The SuperAdmin edits them all before creating. The API writes exactly those rows, restoring `__REDACTED__` leaves from the current tenant's matching Category + Target row. It rejects any other category, and rejects the tenant if the minimum is not met.

The endpoint (`POST /v1/admin/tenants/{id}/clone`) and `CloneTenantRequest` / `DuplicateTenantResponse` were kept, with a new `payloadJson` shape. That avoided a CoreLibs Contracts and Api.Client release.

## Consequences

- Web and API must deploy together: an old Web payload fails API validation, and a new Web payload is misread by an old API.
- `NewTenantSettingsRequirements` (API) and `NewTenantAdminService.BuildDraft` (Web) describe the same minimum and must change together.
- Everything not on either list, including templates, principals and InternalServiceAuth, is configured after creation in Tenant Settings. `Layout:ServiceName` is required, set from the page's Service name field.
- Copied template HostMappings almost always point at the current tenant's own templates and are rejected by the ownership check; the SuperAdmin clears them. Mappings are only checked (and accepted) when the new tenant shares the current tenant's EA database, because the validator cannot reach another database before the tenant exists.
- Renaming the route or contracts to "create" is a later, coordinated CoreLibs + Api.Client change.
