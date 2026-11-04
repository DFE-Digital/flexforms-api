# Dual databases: TenantConfig and EA

- **Status:** Accepted (evident in implementation)
- **Date:** Unknown (present by the 2026 multi-tenant conversion)
- **Provenance:** **Verified** in code/README. Original written ADR not found. The multi-tenant SaaS conversion moved tenants off large `appsettings` tenant blocks.

## Context

The API serves many products. Auth, connection strings, Notify IDs, and feature flags differ per tenant and must change without redeploying the host.

## Decision

- **TenantConfig** SQL (`TenantConfigDbContext`, schema `tenantconfig`) stores tenants, hostnames, origins, principals, and JSON settings (encrypted secrets).
- **EA** SQL (`ExternalApplicationsContext`, schema `ea`) stores users, templates, applications, files. Connection string is per tenant (shared or isolated).

Host `appsettings` holds platform Entra, bootstrap, and `GlobalConfiguration` for CoreLibs DI. It is not the tenant catalogue.

## Alternatives

Not recorded in an ADR. The prior model was configuration folders / `APPLICATION_NAME` on Web and large API `appsettings` tenant arrays (**Historical**).

## Consequences

- Hot reload via `DatabaseTenantConfigurationProvider`.
- Isolated EA DBs must be migrated separately.
- Secret encryption depends on Data Protection ring identity remaining stable.
