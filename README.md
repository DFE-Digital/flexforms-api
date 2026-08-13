# FlexForms API

Backend for **FlexForms** — a multi-tenant, template-driven form platform for GOV.UK services.

Tenants (products such as Transfers, Visits, LSRP) share one API. Each tenant’s configuration, auth, connection strings, and form templates are stored in the database and resolved per request. The companion frontend is [flexforms-web](https://github.com/DFE-Digital/flexforms-web).

---

## Features

- **Multi-tenant SaaS** — TenantConfig database + per-tenant EA data; hostname / `X-Tenant-ID` / Origin resolution
- **JSON template engine** — Versioned schemas rendered by the Web form engine
- **Roles & permissions** — SuperAdmin (platform), Admin / User / custom roles (tenant), claim-based grants
- **Token exchange** — DfE Sign-In / Entra SSO / test / internal service → tenant-scoped API JWT
- **Secure files** — Azure File Share + ClamAV scan via Azure Service Bus
- **GOV.UK Notify** — Email for submit, invites, feedback
- **Real-time notifications** — Azure SignalR
- **Audit** — SQL Server temporal tables on `ea` entities
- **Redis + memory cache** — Tenant-prefixed keys
- **NSwag Api.Client** — Strongly typed .NET client for Web and other consumers
- **Request tracing** — Correlation id, structured Serilog → Application Insights, enriched `ExceptionResponse`, login audit logs

---

## Architecture overview

Clean Architecture / DDD:

| Layer | Project | Purpose |
|-------|---------|---------|
| Presentation | `GovUK.Dfe.FlexForms.Api` | REST, SignalR, auth, middleware, Swagger |
| Application | `GovUK.Dfe.FlexForms.Application` | MediatR CQRS, validators, consumers, domain event handlers |
| Domain | `GovUK.Dfe.FlexForms.Domain` | Aggregates, tenancy entities, interfaces, role rules |
| Infrastructure | `GovUK.Dfe.FlexForms.Infrastructure` | EF Core, migrations, tenant config provider, encryptor |
| Utilities | `GovUK.Dfe.FlexForms.Utils` | Shared helpers |
| Client SDK | `GovUK.Dfe.FlexForms.Api.Client` | Generated HTTP client + token exchange handlers |

```mermaid
flowchart LR
    subgraph Clients
        Web["FlexForms Web"]
        Platform["Platform callers<br/>(MI / SP)"]
    end

    subgraph Azure
        SB["Azure Service Bus"]
        FS["Azure File Share"]
        ASR["Azure SignalR"]
        Redis["Redis"]
        SQL_TC["SQL: TenantConfig"]
        SQL_EA["SQL: EA data<br/>(shared or per-tenant)"]
    end

    subgraph External
        Notify["GOV.UK Notify"]
        ClamAV["ClamAV / file-scanner"]
        IdP["DfE Sign-In / Entra"]
    end

    subgraph API["FlexForms API"]
        MW["TenantResolutionMiddleware"]
        Ctrl["Controllers"]
        Hub["NotificationHub"]
        App["Application / MediatR"]
        Dom["Domain"]
        Infra["Infrastructure"]
        TCP["DatabaseTenantConfigurationProvider"]
    end

    Web -->|REST + X-Tenant-ID| MW
    Web -->|WebSocket| Hub
    Platform -->|PlatformBearer| Ctrl
    MW --> TCP
    TCP --> SQL_TC
    MW --> Ctrl
    Ctrl --> App
    App --> Dom
    App --> Infra
    Infra --> SQL_EA
    Infra --> Redis
    Hub --> ASR
    App --> SB
    App --> FS
    App --> Notify
    ClamAV --> SB
    IdP -.->|tokens exchanged| Ctrl
```

### Dual-database model

| Database | EF context | Schema | Contents |
|----------|------------|--------|----------|
| **TenantConfig** | `TenantConfigDbContext` | `tenantconfig` | Tenants, settings JSON, hostnames, frontend origins, principals |
| **EA** | `ExternalApplicationsContext` | `ea` | Users, roles, memberships, templates, applications, files, permissions |

Host always uses `ConnectionStrings:TenantConfigDatabase`. Each tenant’s EA connection comes from TenantSettings category `ConnectionStrings` (Target Shared/Api) → `DefaultConnection`. Tenants may share one EA database or use isolated DBs.

---

## Multi-tenancy

### How a request gets a tenant

```mermaid
sequenceDiagram
    participant Client
    participant MW as TenantResolutionMiddleware
    participant TCP as TenantConfigurationProvider
    participant TC as TenantConfig DB

    Client->>MW: Request
    alt X-Tenant-ID header present
        MW->>TCP: GetTenant(Guid)
    else Origin header
        MW->>TCP: GetTenantByOrigin
    else
        MW-->>Client: 400 Tenant required
    end
    TCP->>TC: Cached catalogue
    MW->>MW: ITenantContextAccessor.CurrentTenant
    Note over MW: Bypasses: /swagger, /health,<br/>/v1/tenant-config, /v1/host-config
```

1. Prefer **`X-Tenant-ID`** (GUID).
2. Else map **`Origin`** → `TenantFrontendOrigins`.
3. Set scoped `ITenantContextAccessor` and use that tenant’s EA connection string.

**Hostname resolve** (for Web bootstrap): `GET /v1/tenant-config/resolve?hostname=` uses `TenantHostnames` (no scheme).

### TenantConfig tables

| Table | Purpose |
|-------|---------|
| `Tenants` | Id, Name, IsActive |
| `TenantSettings` | Category × Target (`Shared` / `Api` / `Web`) JSON; `IsSecret` encrypted |
| `TenantHostnames` | Host → tenant (e.g. `transfers.dev-flexforms…`) |
| `TenantFrontendOrigins` | CORS origins |
| `TenantPrincipals` | Managed Identity / SP / API key object id → tenant (config consume) |

### TenantSettings targets

| Target | Used by |
|--------|---------|
| `Shared` | Merged into both Api and Web config snapshots |
| `Api` | API runtime (`DatabaseTenantConfigurationProvider`, target `Api`) |
| `Web` | Consumed by Web via `GET /v1/tenant-config/tenants/{id}?target=Web` |

Common categories: `ConnectionStrings`, `AzureAd`, `DfESignIn`, `EntraSso`, `Authorization`, `ApplicationTemplates`, `Email`, `FileStorage`, `FormEngine` (Web), `Layout` (Web), `InternalServiceAuth`, …

Secrets (`IsSecret = 1`) are encrypted with ASP.NET Data Protection.

### Configuration provider

`DatabaseTenantConfigurationProvider` (hosted service):

- Loads active tenants + settings on a timer (~60s) and on `POST /v1/admin/tenants/refresh`
- Decrypts secrets, flattens JSON into `IConfiguration` on `TenantConfiguration`
- Indexes by tenant Id and frontend origin
- Notifies auth registry / OIDC reloaders on change

Tests / codegen can use `TenantConfigSource=AppSettings` + `OptionsTenantConfigurationProvider`.

### TenantPrincipals

Maps Azure AD **oid** / **appid** of a workload identity to a tenant. Used when Web (or another service) calls `GET /v1/tenant-config` — the tenant is resolved from the caller’s JWT, never trusted from a client-supplied id alone.

---

## Authentication and authorisation

### Schemes

| Scheme | Use |
|--------|-----|
| `CompositeScheme` | Default; dispatches ApiKey / mTLS / `TenantBearer` |
| `TenantBearer` | User JWTs (HS256 from TokenSettings) + Entra service tokens |
| `ApiKey` | `X-Api-Key` |
| `Mtls` | Client certificate |
| `PlatformBearer` | Platform Entra app (`Platform:AzureAd`) for host-config / tenant-config ops |
| `HubCookie` | Short-lived cookie for SignalR |

### Token exchange

`POST /v1/tokens/exchange` (policy `ServiceCallers`):

1. Caller presents a machine credential + subject IdP token (DfE Sign-In / Entra SSO / test / internal headers).
2. API validates the subject, finds or creates `User`, ensures `TenantMembership`.
3. Issues a tenant-scoped user JWT with role + permission claims.

Web’s Api.Client uses this on every user session (`RequestTokenExchange`).

**Login audit:** successful exchange logs `UserEmail`, `TenantId`, `TenantName`, `Role`, and `TemplateCount` (structured properties for App Insights).

### Roles

| Role | Scope | Notes |
|------|-------|-------|
| **SuperAdmin** | Platform | Well-known global role id / name `SuperAdmin`. Tenant Settings UI/API. Not tenant-assignable. |
| **Admin** | Tenant | Per-tenant `Roles` row (`TenantId` set). Full tenant admin. Assignable by SuperAdmin. |
| **User** | Tenant | Default self-registration membership. |
| **Custom** | Tenant | Named roles + `RolePermissions`. |
| **Caseworker** | Legacy | Not assignable; prefer custom roles. |

**Important:** Global `Roles` row named `Admin` with `TenantId = NULL` is the **platform SuperAdmin** shell (`RoleConstants.AdminRoleId`). Tenant Admin assignment must use the **tenant-scoped** Admin `RoleId`, never that global id.

Source of truth for “who is Admin in this tenant”: **`TenantMemberships`** → tenant role. Token exchange elevates to SuperAdmin when `Users.RoleId` is the platform admin GUID.

### Permission claims

Format: `{ResourceType}:{ResourceKey}:{AccessType}`  
Examples: `Template:Any:Manage`, `User:Any:Manage`, `Template:{guid}:Read`.

Merged from `RolePermissions` + user `Permissions` overrides (`UserPermissionClaimProvider`). Evaluated by `PermissionClaimEvaluator` / policy handlers (`CanManageUsers`, `CanCreateTemplate`, …).

### Tenant consistency

Bearer claim `tenant_id` must match the resolved request tenant. Cross-tenant tokens are rejected.

---

## Observability and request tracing

Structured logging uses **Serilog** with `Enrich.FromLogContext()` and an Application Insights sink (`Telemetry/ExceptionTrackingTelemetryConverter`). The default App Insights `ILogger` provider is disabled so exceptions and traces share one pipeline with searchable `customDimensions`.

### CoreLibs building blocks

From `GovUK.Dfe.CoreLibs.Http` (local project reference in dev; NuGet in CI):

| Component | Role |
|-----------|------|
| `AddCorrelationId()` / `UseCorrelationId()` | Registers `ICorrelationContext` + `IRequestTelemetryContext`; ensures `x-correlationId` header |
| `GlobalExceptionHandlerMiddleware` | Standard JSON errors, **ErrorId**, merges telemetry onto `ExceptionResponse` |
| `LogContextKeys` | Canonical scope names: `CorrelationId`, `ErrorId`, `TenantId`, `TenantName`, `UserEmail`, `UserId`, `ServiceName` |
| `ExceptionResponse` | First-class `tenantId`, `tenantName`, `userEmail`, `correlationId`; product extras in `context` |

Product-specific dimensions (`TemplateId`, `ApplicationReference`, …) are **not** in CoreLibs — see FlexForms types below.

### FlexForms telemetry (API)

| Type | Location | Purpose |
|------|----------|---------|
| `RequestTelemetryEnrichmentMiddleware` | After `UseAuthentication` / `UseAuthorization` | Fills CoreLibs + FlexForms scopes for all subsequent logs |
| `IFlexFormsRequestScope` | `Telemetry/FlexFormsRequestScope.cs` | `TemplateId`, `ApplicationId`, `ApplicationReference` |
| `FlexFormsLogContextKeys` | `Telemetry/FlexFormsLogContextKeys.cs` | App Insights property names for form context |
| `ExceptionTrackingTelemetryConverter` | `Telemetry/` | Prefers Serilog structured properties; regex fallback only |
| `HeaderForwardingHandler` (Api.Client) | Forwards `X-Template-Id`, `X-Application-Reference` from Web session/headers |

`SharedPostProcessingAction` on the global exception handler copies FlexForms scope into `ExceptionResponse.Context` so Web filters can log `TemplateId` from API errors.

### Request pipeline (middleware order)

```mermaid
flowchart TD
    A[Forwarded headers] --> B[TenantResolutionMiddleware]
    B --> C[CORS / security headers]
    C --> D[UseCorrelationId]
    D --> E[GlobalExceptionHandler]
    E --> F[Routing]
    F --> G[Authentication]
    G --> H[Authorization]
    H --> I[RequestTelemetryEnrichmentMiddleware]
    I --> J[Controllers / SignalR]
```

Tenant resolution scopes `TenantId` / `TenantName` early; enrichment after auth adds user claims and template/application headers from Web.

### ExceptionResponse shape (client / support)

```json
{
  "errorId": "P-123456",
  "statusCode": 500,
  "message": "Something went wrong",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "tenantId": "...",
  "tenantName": "...",
  "userEmail": "user@example.org",
  "context": {
    "TemplateId": "...",
    "ApplicationReference": "..."
  }
}
```

### Support queries (Application Insights)

```kusto
union traces, exceptions
| where customDimensions.CorrelationId == "<guid>"
| project timestamp, cloud_RoleName, message,
          customDimensions.ErrorId, customDimensions.TenantId,
          customDimensions.UserEmail, customDimensions.TemplateId
| order by timestamp asc
```

More examples: `DfE.CoreLibs.Http/ExceptionHandler.md` in the CoreLibs repo.

---

## Domain model (`ea`)

```mermaid
erDiagram
    User ||--o{ TenantMembership : has
    Role ||--o{ TenantMembership : grants
    Role ||--o{ RolePermission : defines
    User ||--o{ Permission : overrides
    User ||--o{ Application : creates
    Template ||--o{ TemplateVersion : versions
    Template ||--o{ TemplatePermission : access
    TemplateVersion ||--o{ Application : used_by
    Application ||--o{ ApplicationResponse : answers
    Application ||--o{ File : attachments
    Template }o--|| TenantHint : TenantId

    User {
        guid UserId PK
        guid RoleId FK
        string Email
        string ExternalProviderId
    }
    Role {
        guid RoleId PK
        string Name
        guid TenantId "null = global"
        bit IsSystem
    }
    TenantMembership {
        guid Id PK
        guid TenantId
        guid UserId
        guid RoleId
        bit IsActive
    }
    Template {
        guid TemplateId PK
        string Name
        guid TenantId
        bit IsLive
    }
```

Templates belong to a tenant via `Template.TenantId` and/or TenantSettings HostMappings (`ApplicationTemplates` / Web `Template`). Catalogue logic: `TenantTemplateCatalogue`.

---

## API surface (v1)

| Area | Prefix | Examples |
|------|--------|----------|
| Applications | `/v1/applications`, `/v1/me/applications` | Create, responses, submit, contributors, files |
| Templates | `/v1/templates` | CRUD versions, live flag, grant-all-users |
| Users | `/v1/users` | Register, assign role, tenant users, permissions |
| Roles | `/v1/roles` | Custom roles + RolePermissions |
| Tokens | `/v1/tokens/exchange` | IdP → API JWT |
| Notifications | `/v1/notifications` | Redis-backed notifications |
| Tenant admin | `/v1/admin/tenants` | Refresh, list, seed, get/upsert settings |
| Tenant config | `/v1/tenant-config` | Consume config, resolve hostname, get by id |
| Host config | `/v1/host-config` | Platform bootstrap for Web |
| Hub auth | hub ticket endpoints | SignalR cookie bridge |
| Feedback | `/v1/userfeedback` | Support / feedback emails |

Swagger: `https://localhost:7089/swagger` (see `launchSettings.json`).

---

## Messaging, files, SignalR

```mermaid
flowchart LR
    Upload["Upload file command"] --> FS["Azure File Share"]
    Upload --> Pub["Publish ScanRequestedEvent"]
    Pub --> SB["Service Bus topic"]
    SB --> Scanner["rsd-file-scanner-function"]
    Scanner --> ClamAV["ClamAV API"]
    Scanner --> SB2["ScanResultEvent"]
    SB2 --> Consumer["ScanResultConsumer"]
    Consumer --> Meta["Update File scan status"]
```

- **Shared** Service Bus namespace and SignalR resource for all tenants; tenant stamped on messages (`TenantAwareEventPublisher` / `TenantContextConsumeFilter`).
- File storage is tenant-aware (`TenantAwareFileStorageService`).

---

## Application layer patterns

- **MediatR** commands/queries with FluentValidation, rate limiting, exception behaviours.
- Feature folders: `Applications`, `Templates`, `Users`, `Roles`, `TenantAdmin`, `TenantConfig`, `Notifications`, `Consumers`.
- Domain events → handlers (email, scan request, cache invalidation).
- Key services: `TenantMembershipService`, `TenantRoleService`, `RolePermissionService`, `ClaimBasedPermissionCheckerService`, `TenantTemplateCatalogue`.

---

## Local development

### Prerequisites

- .NET 10 SDK
- Access to TenantConfig SQL (+ EA SQL if not using LocalDB)
- Redis (or configure memory-only for smoke tests)
- User secrets for platform Entra + connection strings

### Configuration

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:TenantConfigDatabase` | TenantConfig SQL |
| `TenantConfigSource` | `Database` (default) or `AppSettings` |
| `Platform:AzureAd` | Platform Bearer for host/tenant-config |
| `MassTransit` / Service Bus | Messaging (or `SkipMassTransit` for codegen) |
| `DataProtection` | Secret settings encryption |
| `GlobalConfiguration:ApplicationInsights:ConnectionString` | Serilog → App Insights sink |

Per-tenant secrets and connections live in **TenantConfig**, not only in appsettings.

### Local project references (development)

While developing against unreleased CoreLibs telemetry:

- `GovUK.Dfe.FlexForms.Api` → project reference to `DfE.CoreLibs/src/GovUK.Dfe.CoreLibs.Http`
- `GovUK.Dfe.FlexForms.Api.Client` → same CoreLibs project reference
- `flexforms-web` → project references to local Api.Client + CoreLibs.Http

CI/publish should restore **NuGet** package versions once CoreLibs is released and Api.Client is bumped.

### Run

```bash
dotnet run --project src/GovUK.Dfe.FlexForms.Api
```

HTTPS: `https://localhost:7089`

After editing TenantSettings in SQL, call:

```http
POST /v1/admin/tenants/refresh
```

(as an interactive Admin/SuperAdmin user JWT), or wait for the provider refresh interval.

### Migrations

```bash
# EA schema (ea)
dotnet ef migrations add <Name> --project src/GovUK.Dfe.FlexForms.Infrastructure --context ExternalApplicationsContext

# TenantConfig schema
dotnet ef migrations add <Name> --project src/GovUK.Dfe.FlexForms.Infrastructure --context TenantConfigDbContext --output-dir Migrations/TenantConfig
```

### Scripts

See `scripts/` for TenantConfig import helpers (Web/Api settings upsert). Ensure HostMappings only list that tenant’s template GUIDs on shared EA databases.

---

## Security checklist

| Concern | Behaviour |
|---------|-----------|
| Tenant isolation | Middleware + `tenant_id` claim match + membership checks |
| Config consume | Principal → `TenantPrincipals` (no client-chosen tenant) |
| Secret settings | Encrypted at rest; SuperAdmin-only read/write decrypted values |
| Admin APIs | Interactive user JWT required where noted (not pure machine tokens) |
| CORS | Only `TenantFrontendOrigins` |
| Platform ops | `PlatformBearer` + Entra app roles (`Platform.Host.Read`, `Platform.TenantConfig.Read`) |
| Permissions | Claim policies; Admin bypass within tenant |
| Error responses | Global handler; ErrorId + correlation + tenant/user on every unhandled exception |
| Logging | No PII beyond email and ids needed for support; template/application ids in FlexForms scope only |

---

## Related repositories

| Repo | Role |
|------|------|
| [flexforms-web](https://github.com/DFE-Digital/flexforms-web) | Razor Pages UI + form engine |
| [rsd-file-scanner-function](https://github.com/DFE-Digital/rsd-file-scanner-function) | AV scan worker |
| [rsd-clamav-api](https://github.com/DFE-Digital/rsd-clamav-api) | ClamAV sidecar/API |
| [DfE.CoreLibs](https://github.com/DFE-Digital/DfE.CoreLibs) | Shared contracts, security, caching, **Http** (correlation, exception handler, SaaS log keys) |

See also `DfE.CoreLibs.Http/ExceptionHandler.md` for exception middleware configuration and KQL playbooks.

---

## Tests

```bash
dotnet test GovUK.Dfe.FlexForms.Api.sln
```

Unit + integration projects under `src/Tests/`.
