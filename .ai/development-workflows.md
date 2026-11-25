# Development workflows

## Local run

Prerequisites: .NET 10 SDK, TenantConfig SQL, Redis (or memory-only for smoke tests), user secrets for platform Entra.

```bash
dotnet run --project src/GovUK.Dfe.FlexForms.Api
```

HTTPS: `https://localhost:7089` (see `launchSettings.json`). Swagger at `/swagger`. Keep `Microsoft.OpenApi` on **2.x** with Swashbuckle 10 (see known-pitfalls #39); 3.x makes `/swagger` 500.

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

Prism outbox acceptance tests need Docker: `PrismOutboxAcceptanceTests.cs`. So do `Security/TestAuthAuthenticatorStoreSqlServerTests.cs`, which share the same SQL Server container (`SqlServerOutboxFixture.CreateTenantConfigDatabaseAsync`) and check the authenticator store's cross-replica guarantees (unique index, `SetupId` concurrency token, removal retries) with one DbContext per "replica". EF InMemory enforces neither unique indexes nor real cross-context conflicts, so keep those checks on SQL Server.

Do not swap Testcontainers SQL for an in-memory provider when testing outbox/temporal behaviour.

The CoreLibs `CustomWebApplicationDbContextFactory` strips every `DbContextOptions` registration and only re-adds contexts listed in `SeedData` (EA). `CustomWebApplicationDbContextFactoryCustomization` registers `TenantConfigDbContext` on the EF InMemory provider so handlers that depend on it (Test Authentication authenticators) resolve in integration tests. The "with rate limiting" customization does not.

### Two-tenant host with real authentication

`Tests.Common/TwoTenant/TwoTenantApi` (**Verified**) runs the API with two tenants, `TestTenant.A` and `TestTenant.B`, and the real authentication pipeline: no mock bearer handler. Use it as an xUnit class fixture.

- `SignInAsync(tenant, email)` calls the real `POST /v1/tokens/exchange` the way Web does (the tenant's service API key in `X-Api-Key`, a subject token in the body) and returns the user JWT the API signed. Only the identity provider is a stand-in (`TestExternalIdentityValidator`).
- `Client(tenant, bearerToken?, apiKey?)` sets `X-Tenant-ID` and nothing else, so a client with neither credential really sends none.
- `TwoTenantApi` gives each tenant its own signing key, issuer and audience; `TwoTenantApiWithSharedSigning` gives both the same, so `tenant_id` is the only barrier.
- `TwoTenantSeeder` fills one shared EA database with the same shape for each tenant: system Admin and User roles, Caseworker, TemplateManager and UserManager roles with their grants, a live template, and an owner's application with a response and a file. `BothTenantsEmail` is Admin in A and User in B; `SuperAdminEmail` is a platform SuperAdmin with no membership. Ids and emails are on `TestTenant`.
- Process environment and the in-memory stand-ins (Redis, email, storage, bus) come from `IntegrationTestServices`, shared with the default host.
- A file upload endpoint only accepts `multipart/form-data`; routing answers 415 for any other body before authentication runs.

### Permission matrix

`Api.Tests.Integration/TwoTenant/PermissionMatrix` (**Verified**) records, for every controller action, who gets through (on a `MatrixApi`, a `TwoTenantApi` with 12 tenant A principals: anonymous, member, contributor, owner, caseworker, form manager (Manage on one form only), user manager, admin, SuperAdmin, the Web service key, a service key with the Admin role, and the file-validation service).

- **Layer A** (`PermissionMatrix.Endpoints`, `PermissionMatrixTests`): the authorization policy's decision per principal. `PolicyRecorder` wraps the API's `IAuthorizationMiddlewareResultHandler` and records the result for requests carrying `X-Test-Request-Id`; with `X-Test-Policy-Only` an allowed request stops after authorization with 204, so the matrix runs every endpoint without side effects. `PlatformBearer` keeps the real handler with an empty offline OpenID configuration, so platform endpoints show only that no tenant credential gets through.
- **Layer B** (`HandlerCases.All`, `HandlerOutcomeTests`): real requests to the handler-gated TenantAdmin, Users, Roles and Templates endpoints from each principal Layer A lets through, and the status each gets. Principals run in `HandlerCases.Order`; writes aim at their own targets (`TestTenant.SpareMemberId(n)`, a form or role created in the case) so cases can't change each other's results.
- **Adding an endpoint:** add a row to `PermissionMatrix.Endpoints` (key `Controller.Method`, as in `ExpectedSecurity.json`) with its `Allowed` principals, or `Open = true` for `[AllowAnonymous]`. Without one, `EveryControllerActionHasExactlyOneRow`, `EveryControllerActionInTheApiAssemblyHasARow` and `TheRowsMatchExpectedSecurityJson` fail. If the endpoint's handler decides more than its policy, add a `HandlerCases` entry too.
- A service principal on an API key has no email or app id, so rate-limited commands (`IRateLimitedRequest`, for example register, create a template version) answer it 400 or 401 before the handler runs.

Integration-test gotchas (**Verified**):

- The default host (`CustomWebApplicationDbContextFactoryCustomization`) replaces authentication with a mock bearer handler, and its client always sends a bearer token. A caller with no claims there gets 403 (or 401 from a handler that needs an email), never an authentication 401. Tests named `…_WhenCallerHasNoClaims` check exactly that. For real 401s, real tokens or anything crossing tenants, use `TwoTenantApi` (above).
- `factory.TestClaims` is read **once**, when the mock bearer handler is first built (CoreLibs registers it as a singleton). Assigning a new list later in a test is silently ignored and the next request still runs as the first caller. To switch callers, change the same list: `factory.TestClaims.Clear(); factory.TestClaims.AddRange(...)` (see `CallAs` in `UsersControllerPermissionsTests`).
- Only the clients registered in `CustomWebApplicationDbContextFactoryCustomization` are real. Any other client interface a test asks for is an AutoFixture mock that returns nulls and never throws. Register a new client there before testing its controller (`IRolesClient` was missing until chunk 1.6).
- The test host does not cache `/me/permissions`, so an HTTP test can't show a cache clear; check `IUserCacheInvalidator` calls in a handler unit test.
- The in-memory TenantConfig store starts empty for each factory. Add a `TenantEntity` for the test tenant before writing settings, or the writer throws `KeyNotFoundException` and the API answers 404 (see `SeedTenantAsync` in `TenantAdminControllerSettingsTests`).
- The API registers `ITenantConfigurationRefreshState` only when `TenantConfigSource` is `Database`. The test host loads tenants from `TestTenantConfigurationProvider`, so the customization registers `TestTenantConfigurationRefreshState`; without it `effective-config` and `platform` fail to resolve their handlers.

## NSwag / Api.Client

Local build regenerates the client. Use `SkipMassTransit` / CodeGeneration appsettings if the host must start without bus. After contract changes, bump the NuGet consumed by Web and Prism.

To build against a CoreLibs Contracts version that is not yet on nuget.org, `dotnet pack` it into a local folder and restore with `-p:RestoreAdditionalProjectSources=<folder>`. Delete that version from `~/.nuget/packages` afterwards so the published package is used once it exists.

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

Coupled go-lives: promote this API first. Web's production deploy can set `required_api_release` to this repo's production tag and will wait until that GitHub release is **Latest** before rolling Web.
