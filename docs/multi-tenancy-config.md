# Multi-tenant configuration

FlexForms is a SaaS platform: each tenant's runtime settings live in the **TenantConfig** SQL database and are loaded into an in-memory catalogue that hot-reloads without restarting the API or Web apps.

`appsettings.json` remains useful for local bootstrap and one-off seeding, but **production runtime configuration is database-backed**.

## Where settings live

| Store | Purpose |
| --- | --- |
| `TenantConfig` database (`tenantconfig` schema) | Authoritative per-tenant settings, hostnames, frontend origins, principals |
| In-memory catalogue (`ITenantConfigurationProvider`) | Lock-free snapshot refreshed on a timer and on admin refresh |
| `appsettings` `Tenants` section | Initial seed source only (`POST /v1/admin/tenants/seed`) |

Each tenant has:

- **Settings rows** — JSON blobs keyed by `Category` + `Target` (`Shared`, `Api`, `Web`)
- **Hostnames** — public DNS names used by the Web app to resolve `X-Tenant-ID`
- **Frontend origins** — CORS / OIDC redirect origins

Secret categories (`ConnectionStrings`, `Authorization`, `DfESignIn`, `EntraSso`, `TestAuthentication`, etc.) are **always encrypted** at rest via Data Protection, regardless of the UI checkbox.

## Resolution

Clients must send `X-Tenant-ID` (GUID) on API requests. The middleware resolves the tenant from:

1. `X-Tenant-ID` header
2. `Origin` header (matched against registered frontend origins)

The Web app resolves tenant id from `X-Tenant-ID`, `?tenantId=`, or the public request hostname (via platform API hostname lookup, with a short-lived in-process cache).

Requests with missing or unknown tenants return `400`.

## SuperAdmin operations

Interactive SuperAdmins (own tenant only) can use:

| Endpoint | Purpose |
| --- | --- |
| `GET /v1/admin/tenants/{id}/settings` | List decrypted settings |
| `POST /v1/admin/tenants/{id}/settings` | Upsert a category (validated JSON, forced secrets, audit log) |
| `POST /v1/admin/tenants/refresh` | Force catalogue reload |
| `GET /v1/admin/tenants/{id}/effective-config` | Preview effective auth scheme, hostnames, cache metadata |
| `GET /v1/admin/tenants/{id}/export` | Promotion bundle (secrets redacted) |
| `POST /v1/admin/tenants/{id}/import` | Apply promotion bundle |
| `GET /v1/admin/tenants/{id}/settings/audit` | Recent setting change audit trail |

After changing settings in Tenant Settings (Web), the page refreshes the API catalogue **and** clears the Web hostname cache so DNS mapping updates apply immediately.

## Health endpoints

The API exposes unauthenticated probes (bypass tenant resolution):

| Path | Behaviour |
| --- | --- |
| `/health`, `/healthz` | All health checks |
| `/liveness` | Process is up (`self` check only) |
| `/readiness` | SQL readiness (`TenantConfig` + application databases) |

## Auth provider consistency

Bearer tokens, API keys, and mTLS client certificates are resolved via `ITenantAuthProviderRegistry`. When a tenant context is present, the matched provider **must belong to that tenant** — cross-tenant credential replay is rejected.

## Operational notes

- Rotate secrets via Tenant Settings; changes propagate within the refresh interval (default 60s) or immediately after admin refresh.
- Use **export/import** to promote non-secret configuration between environments; re-enter secrets in the target environment.
- Keep platform-wide values (e.g. Application Insights connection string) at the host `appsettings` root — not per tenant.
- When adding a tenant, ensure hostname and frontend origin are unique across the platform.

## Legacy appsettings shape (seed only)

```jsonc
{
  "Tenants": {
    "11111111-1111-1111-1111-111111111111": {
      "Id": "11111111-1111-1111-1111-111111111111",
      "Name": "Alpha",
      "Hostnames": ["alpha.example.com"],
      "ConnectionStrings": { "DefaultConnection": "..." },
      "DfESignIn": { "ClientId": "..." },
      "Frontend": { "Origin": "https://alpha.example.com" }
    }
  }
}
```

Run `POST /v1/admin/tenants/seed` once to populate the database from this structure when migrating from file-based config.
