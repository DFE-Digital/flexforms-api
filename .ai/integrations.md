# Integrations

**Verified** unless marked otherwise.

## Azure Service Bus topics

Constants in CoreLibs `TopicNames`:

| Topic | Event | Publisher | Consumer |
|-------|-------|-----------|----------|
| `file-scanner-requests` | `ScanRequestedEvent` | API (`FileUploadedDomainEventHandler`) | `rsd-file-scanner-function` |
| `file-scanner-results` | `ScanResultEvent` | file-scanner-function | **API and Web** `ScanResultConsumer` |
| `flexforms-prism` | `ApplicationProjectionRequestedEvent`, `TemplateVersionPublishedEvent` | API outbox | Prism `ProjectionFunction` |
| `transfer-application-submitted` | `TransferApplicationSubmitted` | Schema/event mapping (tenant) | External RSD consumers |

Tenant is stamped on messages (`TenantAwareEventPublisher` / `TenantContextConsumeFilter`). Shared namespace for all tenants — isolation is headers/metadata, not a bus per tenant.

**Do not remove either ScanResult consumer.** API deletes infected files from storage/DB and updates metadata. Web handles session/UX blacklist and user-facing notifications. **Verified:** both classes exist.

Locally, `InstanceIdentifierHelper` prevents one developer’s consumer from stealing another’s messages (`MessageNotForThisInstanceException`).

## File storage

Azure File Share (or local disk in Development) via `GovUK.Dfe.CoreLibs.FileStorage`. ADR: `docs/adrs/20251125_use_azure_file_share.md`. Defender for Cloud does **not** scan File Share — that is why ClamAV exists.

Runtime overlays: `TenantAwareFileStorageService`. Host DI uses `GlobalConfiguration:FileStorage`; non-local **must** set this (no first-tenant fallback in deployed environments).

## ClamAV pipeline

API does **not** call ClamAV. Flow:

1. `UploadFileCommandHandler` stores the file.
2. `FileUploadedDomainEventHandler` builds a SAS URI (`IAzureSpecificOperations`) and publishes `ScanRequestedEvent`.
3. Scanner POSTs `/scan/async/url` to ClamAV API (Base64 URL).
4. `ScanResultConsumer` (API) updates file metadata, deletes infected files, notifies via Redis + SignalR.

Scan requests are **not** on the Prism always-outbox list. Default outbox allowlist is empty, so scan publish is direct unless a tenant/host lists `ScanRequestedEvent`. A bus outage can therefore skip scans (**Documented** trade-off).

## SignalR

Hub: `/hubs/notifications` (`Api/Hubs/NotificationHub.cs`). Browser connects to the **API** host, not Web.

- Ticket: `HubAuthController` → `hubauth` cookie.
- Groups: tenant + user (see hub).
- Redis is **cache only**, not a SignalR backplane. Multi-replica in-process SignalR does not fan out. Production needs **Azure SignalR** (`Azure__SignalR__ConnectionString` / `ConnectionStrings:AzureSignalR`). Empty connection string → startup fails outside Development/Local/code generation unless `SignalR:AllowInProcess=true` (only for an environment that will never run more than one replica).
- Redis pub/sub channel `FlexForms:Api:TenantConfigurationChanged` (API replicas only) carries tenant configuration refreshes between replicas. Pub/sub channels are server-wide, not per logical database, so environments that share a Redis server receive each other's messages; that only causes extra refreshes, never wrong data. Web uses its own channels (`FlexForms:TenantConfigurationInvalidated`, `FlexForms:LocalCacheKeyRemoved`); the two apps do not listen to each other's. Web also caches templates and status labels in the shared Redis under `FlexForms:Cache:FormTemplate_*` / `CustomApplicationStatuses_*`; those keys do not collide with the API's tenant-prefixed `FlexForms:Cache:t:{tenantId}:*` keys.
- If Dev and Test share one Azure SignalR resource, set distinct `Azure__SignalR__ApplicationName` (e.g. `flexforms-dev` vs `flexforms-test`).

## Prism

See [`features/prism-projection.md`](features/prism-projection.md). Events are ids-only nudges. Prism re-reads `/v1/internal/prism` with `Prism.Read`. Reporting export policy is proxied via `ReportingExportController` → Prism control API (`PrismReportingExportPolicyService`).

## GOV.UK Notify

`GovUK.Dfe.CoreLibs.Email`. Tenant-aware wrapper `TenantAwareEmailService`. Template IDs in TenantConfig `EmailTemplates`. Placeholder mappings optional.

## Redis

Notifications store + hybrid cache. Keys must be tenant-prefixed (`TenantCacheKeyHelper` / `TenantPrefixedCacheKey`). Operations without tenant context should fail closed. Foreign `t:{otherTenant}:` prefixes must throw.

API cache `GetOrAddAsync` can fall back to SQL on Redis errors. Web session **must not** treat Redis errors as empty session (CoreLibs `SessionDistributedCacheAdapter`).

## SharePoint / Academies

**Verified:** no SharePoint package reference in this repo. CoreLibs `Academies` DTOs exist but FlexForms API does not consume an Academies API. Autocomplete of establishments is a **Web** concern (external HTTP).

## Tenant-operated functions

File validation Azure Functions are **not in this workspace**. Contract: HTTP callback to `IntegrationsController`. Identity from credential, never from body.
