# Architecture

**Verified** against `GovUK.Dfe.FlexForms.Api.sln`, `Program.cs`, and [`README.md`](../README.md).

## Layers

| Layer | Project | Responsibility |
|-------|---------|----------------|
| Presentation | `GovUK.Dfe.FlexForms.Api` | Controllers, middleware, SignalR `NotificationHub`, auth, Swagger |
| Application | `GovUK.Dfe.FlexForms.Application` | MediatR commands/queries, FluentValidation, consumers, domain-event handlers |
| Domain | `GovUK.Dfe.FlexForms.Domain` | Aggregates, tenancy abstractions, domain events, factories |
| Infrastructure | `GovUK.Dfe.FlexForms.Infrastructure` | EF Core, migrations, tenant config provider, encryptor, Playwright HTML |
| Utils | `GovUK.Dfe.FlexForms.Utils` | Host CoreLibs config resolution, cache prefixes, outbox options |
| Client SDK | `GovUK.Dfe.FlexForms.Api.Client` | NSwag client + token exchange handlers (consumed by Web and Prism) |

Entry point: `src/GovUK.Dfe.FlexForms.Api/Program.cs` only.

## CQRS

Handlers live under `Application/{Feature}/Commands|Queries/`. Pipeline: `ValidationBehaviour`, `UnhandledExceptionBehaviour`, `RateLimitingBehaviour`, optional `PerformanceBehaviour` (`ApplicationServiceCollectionExtensions`).

Controllers inject `ISender` and stay thin. After adding `[Authorize]` endpoints, update `src/Tests/GovUK.Dfe.FlexForms.Api.Tests/Security/ExpectedSecurity.json`.

## Dual-database model

| Database | Context | Schema | Contents |
|----------|---------|--------|----------|
| TenantConfig | `TenantConfigDbContext` | `tenantconfig` | Tenants, settings JSON, hostnames, origins, principals |
| EA | `ExternalApplicationsContext` | `ea` | Users, roles, memberships, templates, applications, files, outbox |

Host always uses `ConnectionStrings:TenantConfigDatabase`. Each tenant’s EA connection comes from TenantSettings `ConnectionStrings:DefaultConnection` (Target Shared/Api). Tenants may share one EA database or use isolated DBs.

`DatabaseTenantConfigurationProvider` reloads the catalogue on a timer (~60s) and on `POST /v1/admin/tenants/refresh`. DI resolves `ITenantConfigurationProvider` to `ReplicatedTenantConfigurationProvider`, which wraps it so admin-triggered refreshes are broadcast to every replica over Redis pub/sub (`.ai/known-pitfalls.md` item 34).

**Historical pitfall (verified):** a flatten/JSON error for **one** tenant can abort the **entire** refresh, leaving a stale snapshot so new tenants appear “not configured”. See [`known-pitfalls.md`](known-pitfalls.md).

## Request pipeline (API)

Documented in README; implementation in `Program.cs`:

1. Forwarded headers
2. `TenantResolutionMiddleware` (`Api/Middleware/TenantResolutionMiddleware.cs`)
3. CORS / security headers
4. Correlation id (CoreLibs)
5. Global exception handler
6. Routing → Authentication → Authorization
7. `RequestTelemetryEnrichmentMiddleware`
8. Controllers / SignalR

Bypass tenant resolution for swagger, health, `/v1/tenant-config`, `/v1/host-config`, `/v1/internal/prism/tenants`, diagnostics, OPTIONS, and root probes.

## Persistence patterns

- Generic `IEaRepository<T>` / `EaRepository<T>`.
- `IApplicationRepository.AppendResponseVersionAsync` for transactional response append + `SourceRevision` increment.
- `IQueryObject<T>` for reusable EF filters.
- Domain events on `BaseAggregateRoot`; `DomainEventDispatcherInterceptor` publishes via MediatR after save.
- SQL Server temporal tables on major `ea` entities when `Database.IsSqlServer()` (disabled for SQLite tests). ADR: `docs/adrs/20251125_temporal_tables_for_auditing.md`.

## Controllers

Under `src/GovUK.Dfe.FlexForms.Api/Controllers/`:

`ApplicationsController`, `TemplatesController`, `UsersController`, `RolesController`, `TokensController`, `NotificationsController`, `TenantAdminController`, `TenantConfigController`, `HostConfigController`, `InternalPrismController`, `IntegrationsController`, `HubAuthController`, `UserFeedbackController`, `ReportingExportController`, `DiagnosticsController`.

## Observability

Serilog → Application Insights **2.x** (3.x breaks `Serilog.Sinks.ApplicationInsights`). Correlation header `x-correlationId`. Support search by `ErrorId` then `CorrelationId`. FlexForms-specific dimensions (`TemplateId`, `ApplicationReference`) live in this repo, not CoreLibs.

## What not to introduce

- A second flatten-to-lake path that bypasses Prism.
- Per-tenant MassTransit bus connections or named OIDC schemes at startup (does not scale; **Historical** from the multi-tenant conversion).
- Scanning or Notify I/O on the HTTP thread for upload/submit happy path (scan is async; email is post-commit domain-event handling).
