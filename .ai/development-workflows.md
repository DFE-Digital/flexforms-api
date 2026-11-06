# Development workflows

## Local run

Prerequisites: .NET 10 SDK, TenantConfig SQL, Redis (or memory-only for smoke tests), user secrets for platform Entra.

```bash
dotnet run --project src/GovUK.Dfe.FlexForms.Api
```

HTTPS: `https://localhost:7089` (see `launchSettings.json`). Swagger at `/swagger`.

After editing TenantSettings in SQL: `POST /v1/admin/tenants/refresh` (Admin/SuperAdmin JWT) or wait ~60s.

`TenantConfigSource=AppSettings` + `OptionsTenantConfigurationProvider` for tests/codegen.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/GovUK.Dfe.FlexForms.Infrastructure --context ExternalApplicationsContext
dotnet ef migrations add <Name> --project src/GovUK.Dfe.FlexForms.Infrastructure --context TenantConfigDbContext --output-dir Migrations/TenantConfig
```

**EA migrations must be applied to every tenant EA database.** `script/migrate-databases.sh` only migrates `ConnectionStrings__DefaultConnection`. Missing `AddMassTransitTransactionalOutbox` tables on an isolated tenant DB silently drops outbox events.

Data-only migrations (`ScopeNotificationPermissionsByTenant`, `BackfillTenantNotificationPermissions`) are hand-written with `[DbContext]` + `[Migration]` attributes and no Designer file, because the model snapshot does not change. `BackfillTenantNotificationPermissions` is idempotent and its `Down` is a no-op.

## Tests

| Project | Focus |
|---------|--------|
| Domain.Tests | Aggregates, policies |
| Application.Tests | Handlers, consumers, publishers |
| Api.Tests | Middleware, security golden file, outbox acceptance (Testcontainers SQL) |
| Api.Tests.Integration | Controllers + SignalR |
| Tests.Common | WebApplicationFactory, tokens |

Prism outbox acceptance tests need Docker: `PrismOutboxAcceptanceTests.cs`.

Do not swap Testcontainers SQL for an in-memory provider when testing outbox/temporal behaviour.

## NSwag / Api.Client

Local build regenerates the client. Use `SkipMassTransit` / CodeGeneration appsettings if the host must start without bus. After contract changes, bump the NuGet consumed by Web and Prism.

## TenantConfig scripts

`scripts/` PowerShell import/bootstrap. HostMappings GUIDs must be claimable. SuperAdmin-only to edit mappings. See `scripts/README.md`.

**Historical:** `Export-ApiTenantConfigSql.ps1` cannot run inside the ACA `aspnet` image (no pwsh/SDK). Harvest env from the container; encrypt/export from a workstation with Data Protection blob + Key Vault params.

## Feature work pattern

1. Domain invariant on the aggregate or domain service.
2. Command/query + validator in Application feature folder.
3. Thin controller with existing policy names, or add handler + `ExpectedSecurity.json`.
4. Domain event for side effects.
5. Tests at the layer you changed.
6. Update `.ai/` if behaviour changed.

## Release

Trunk-based: `docs/release-process.md`. Do not rebuild production images. Backport labelled fixes onto `release/x.y`.
