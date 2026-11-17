# Code map (flexforms-api)

**Verified** against source on 2026-11-17. Where to find things, so you can open the right file first. Platform-wide context: [`ecosystem.md`](ecosystem.md).

## Projects

| Project | Path | Key folders |
|---------|------|-------------|
| Api (host) | `src/GovUK.Dfe.FlexForms.Api` | `Program.cs` (only entry point), `Controllers/`, `Middleware/` (`TenantResolutionMiddleware`, `RequestTelemetryEnrichmentMiddleware`, `RequestTimingMiddleware`, `UrlDecoderMiddleware`), `Security/` (schemes, policies, handlers, claim providers, Data Protection), `Hubs/NotificationHub.cs`, `Telemetry/` (tenant-aware App Insights channel, PII masking, 499 processor), `Tenancy/` (`TenantContextAccessor`, `TenantCorsPolicyProvider`), `Filters/` (Result → exception mapping), `Swagger/` |
| Application | `src/GovUK.Dfe.FlexForms.Application` | Feature folders with `Commands/` + `Queries/` (MediatR), `Applications/EventHandlers/` (domain-event side effects), `Consumers/ScanResultConsumer.cs`, `Services/` (cross-feature services), `Security/` (Test Auth OTP + authenticator), `Messaging/` (event catalogue, bus configurator), `Options/` (EventTriggers, SchemaEvents, email types), `Common/Behaviours/` (MediatR pipeline), `ApplicationServiceCollectionExtensions.cs` (MassTransit, outbox, subscriptions) |
| Domain | `src/GovUK.Dfe.FlexForms.Domain` | `Entities/`, `Events/`, `ValueObjects/StronglyTypedIds.cs`, `Factories/` (`UserFactory`), `Services/` (grant rules, `TenantScopedIdentityKey`), `Tenancy/` (tenant abstractions, `TenantSettingTargetOverlap`, secret / SuperAdmin-only / safe category lists, `TenantApiKeyHasher`) |
| Infrastructure | `src/GovUK.Dfe.FlexForms.Infrastructure` | `Database/` (`ExternalApplicationsContext`, `TenantConfigDbContext`, interceptors), `Migrations/` (EA) + `Migrations/TenantConfig/`, `Repositories/` (`EaRepository<T>`, `ApplicationRepository`), `Services/` (tenant config providers, settings reader/writer, encryptors, Prism export proxy, Playwright HTML preview, reference generator), `Caching/` (`RedisAtomicCacheStore`, `RedisRateLimiterFactory`, `TenantAwareDistributedCache`), `Security/` (`AuthConstants`, `PlatformConstants`), `Messaging/Outbox/` |
| Utils | `src/GovUK.Dfe.FlexForms.Utils` | Host CoreLibs config resolution, cache key constants (`FlexFormsCacheKeys`), outbox options, file helpers |
| Api.Client | `src/GovUK.Dfe.FlexForms.Api.Client` | `Generated/` (NSwag, regenerated on local build from `nswag.json`), `Security/` (`TokenExchangeHandler` etc.), `Extensions/` (`AddExternalApplicationsApiClients`), `*ClientJsonSettings.cs` |
| Tests | `src/Tests/*` | Domain.Tests, Application.Tests, Api.Tests (security golden file, middleware, Testcontainers outbox/authenticator tests), Api.Tests.Integration (controllers + SignalR), Tests.Common (factories) |
| Benchmarks | `src/Benchmarks` | BenchmarkDotNet, not in CI |
| Other | `terraform/`, `scripts/` (PowerShell TenantConfig import/export), `script/` (migration shell), `docs/` (manuals, ADRs) | |

## Endpoints → policy → handler

All routes are under `v1` unless absolute. Policy names are verified in `ExpectedSecurity.json`; update it when you change one.

### Applications (`ApplicationsController`)

| Method + route | Policy | MediatR |
|---|---|---|
| POST `Applications` | CanCreateAnyApplication | `CreateApplicationCommand` |
| POST `Applications/{id}/responses` | CanUpdateApplication | `AddApplicationResponseCommand` |
| GET `me/applications` | CanReadAnyApplication | `GetMyApplicationsQuery` |
| GET `Applications/templates/{templateId}` | CanReadAnyApplication | `GetApplicationsByTemplateQuery` |
| GET `Users/{email}/applications` | CanReadAnyApplication | `GetApplicationsForUserQuery` |
| GET `Applications/reference/{ref}` | CanReadAnyApplication | `GetApplicationByReferenceQuery` |
| PUT `Applications/{id}/owner` | TenantAdminUser | `ChangeApplicationOwnerCommand` |
| DELETE `Applications/{id}` | TenantAdminUser | `DeleteApplicationCommand` |
| POST `Applications/{id}/submit` | CanUpdateApplication | `SubmitApplicationCommand` |
| GET / POST `Applications/{id}/contributors`, DELETE `.../contributors/{userId}` | CanReadAnyApplication / CanUpdateApplication | `GetContributorsForApplicationQuery`, `AddContributorCommand` (optional `Permissions`), `RemoveContributorCommand` |
| POST / GET `Applications/{id}/files`, GET `.../files/{fileId}/download`, DELETE `.../files/{fileId}` | CanWriteApplicationFiles / CanReadApplicationFiles / CanDeleteApplicationFiles | `UploadFileCommand`, `GetFilesForApplicationQuery`, `DownloadFileQuery`, `DeleteFileCommand` |
| GET `Applications/{id}/file-validation-gate` | CanReadApplication | `GetApplicationFileValidationGateQuery` |
| GET `Applications/reference/{ref}/preview/download` | CanReadAnyApplication | `GenerateApplicationPreviewHtmlQuery` (Playwright HTML) |

### Templates (`TemplatesController`)

GET `Templates` (CanListTemplates, `GetAccessibleTemplatesQuery`); POST `Templates` (CanCreateTemplate, `CreateTemplateCommand`); GET `{id}/schema`, `{id}/versions`, `{id}/versions/{*versionNumber}`, `{id}/custom-statuses` (CanReadTemplate); POST `{id}/custom-statuses`, PUT `{id}/live`, POST `{id}/grant-all-users`, POST `{id}/versions` (CanCreateTemplate; `UpdateCustomApplicationStatusCommand`, `SetTemplateLiveCommand`, `GrantTemplateAccessToAllUsersCommand`, `CreateTemplateVersionCommand`).

### Users, roles, tokens

| Route | Policy |
|---|---|
| GET `me/permissions` | `[Authorize]` |
| GET/POST/DELETE `me/test-auth-authenticator`, POST `.../setup`, `.../setup/confirm` | `[Authorize]` (+ rate limit) |
| POST `Users/test-auth-authenticator-invites` | CanManageUsers |
| POST `Users/register` | ServiceCallers |
| POST `Users/roles`, GET `Users/access-audit`, GET `Users/tenant`, GET/PUT `Users/{id}/permissions`, PUT `Users/{id}/templates`, DELETE `Users/{id}/tenant` | CanManageUsers |
| GET `Users/created-applications` | Roles SuperAdmin, Admin |
| `Roles` CRUD, `Roles/from-template`, `Roles/{id}/permissions` | Roles SuperAdmin, Admin |
| POST `Tokens/exchange`, `Tokens/test-auth-password`, `.../verify`, `Tokens/test-auth-authenticator/verify`, `.../invite/redeem`, `.../invite/confirm` | ServiceCallers |

### Tenant administration and platform

| Route | Policy |
|---|---|
| `admin/tenants` (GET list, POST `refresh`, GET/PUT `{id}/safe-settings`, GET/POST/DELETE `{id}/settings`, POST `{id}/settings/reveal`, POST `{id}/settings/validate`, GET `{id}/settings/audit`, POST `{id}/clone` (New tenant), POST `{id}/duplicate` (legacy), GET `{id}/effective-config`, GET `{id}/export`, POST `{id}/import`, GET `{id}/health`, GET `settings/cookbook`, GET `event-catalogue`, GET `platform`) | TenantAdminUser (+ SuperAdmin checks inside handlers) |
| POST `admin/tenants/seed` | PlatformTenantConfig |
| `admin/tenants/{tenantId}/reporting-export/*` (tenant default, template policy, decisions, template default, refreshes) | TenantAdminUser → `PrismReportingExportPolicyService` |
| GET `tenant-config` | `[Authorize]` (tenant view for Web) |
| GET `tenant-config/tenants/{id}`, GET `tenant-config/resolve` | PlatformTenantConfig |
| GET `host-config` | PlatformHost |
| `internal/prism/tenants`, `applications`, `applications/{id}/current`, `responses/{id}`, `template-versions/{id}` | PlatformPrismRead |
| POST `integrations/files/{fileId}/validation-result` | CanRecordFileValidation |
| `Notifications` (POST, GET, `unread`, `unread/count`, `category/{c}`, PUT `{id}/read`, `read-all`, DELETE `{id}`, `clear-all`, `category/{c}`, `context/{c}`) | CanRead/Write/DeleteNotifications |
| POST `auth/hub-ticket` (TenantBearer), GET `auth/hub-cookie`, POST `auth/hub-signout` (anonymous) | — |
| POST `UserFeedback` | anonymous |
| GET `diagnostics` | TenantAdminUser |
| `/hubs/notifications` | HubCookie |

## MediatR pipeline

`ApplicationServiceCollectionExtensions`: `ValidationBehaviour` (FluentValidation), `UnhandledExceptionBehaviour`, `RateLimitingBehaviour` (`[RateLimit]` attribute; Redis sliding window), optional `PerformanceBehaviour`. Handlers return `Result<T>`; `Filters/ResultToExceptionFilter` + `ResultExceptionMapper` map failures to HTTP.

## Domain events → handlers (post-commit, `DomainEventDispatcherInterceptor`)

| Event | Handler | Side effects |
|---|---|---|
| `ApplicationCreatedEvent` | `ApplicationCreatedEventHandler` | logging only |
| `ApplicationSubmittedEvent` | `ApplicationSubmittedEventHandler` | Notify email, `EventTriggers:ApplicationSubmitted` |
| `FileUploadedDomainEvent` | `FileUploadedDomainEventHandler` | SAS URI, `ScanRequestedEvent`, `EventTriggers:FileUploaded` |
| `FileValidationRecordedEvent` | `FileValidationRecordedEventHandler` | stored notification (Redis) + SignalR push to the uploader |
| `ContributorAddedEvent`, `ContributorPermissionsGrantedEvent` | matching handlers | contributor emails (`EmailPlaceholderMappings` Metadata keys) |
| `UserCreatedEvent` | `UserCreatedEventHandler` | logging only |
| `ApplicationResponseAddedEvent`, `ApplicationDeletedEvent`, `FileDeletedEvent` | (no dedicated handler today) | |

Prism projection events are **not** domain events: command handlers call `IProjectionEventPublisher` (`ProjectionEventPublisher`) before `SaveChanges` — Create, AddResponse, Submit, Delete, ChangeOwner (`ApplicationProjectionRequestedEvent`), CreateTemplate, CreateTemplateVersion (`TemplateVersionPublishedEvent`). `NoOpProjectionEventPublisher` is used when MassTransit is skipped.

## Key services

| Concern | Type |
|---|---|
| Tenant resolution | `Api/Middleware/TenantResolutionMiddleware`, `Infrastructure/Services/TenantHostnameResolver` |
| Tenant catalogue | `DatabaseTenantConfigurationProvider` (60 s timer) wrapped by `ReplicatedTenantConfigurationProvider` (Redis pub/sub); `OptionsTenantConfigurationProvider` for tests/codegen |
| Settings read/write | `TenantSettingsReader` (Shared + target, ordered by Target name), `TenantSettingsWriterService`, `TenantSettingJsonValidator`, `TenantSettingAuditWriter` |
| New tenant | `CreateTenantCommandHandler`, `NewTenantSettingsRequirements`, `TenantCreatorService` |
| Export/import | `ExportTenantConfigurationQuery`, `ImportTenantConfigurationCommand`, `TenantConfigurationPromotionService` |
| Auth providers | `DatabaseTenantAuthProviderRegistry`, `Security/Schemes/ApiKeyAuthenticationHandler`, `TenantTokenSettingsConfigurator`, `TenantOidcProviderBuilder` |
| Claims | `UserPermissionClaimProvider`, `PermissionClaimProvider` (re-emits invite-scope markers), `TemplatePermissionClaimProvider`, `TenantClaimsTransformation`; Domain `PermissionClaimMerger` (role + user grants, `ContributorInvite:{id}:Scope` markers) and `PermissionClaimEvaluator` |
| Contributors | `AddContributorCommandHandler`, `Applications/ContributorInviteAccess.ResolveAccess` (invite checkboxes → grants), `IUserFactory.RemoveApplicationAndFilePermissions` (re-invite replaces grants); see `features/permissions-and-roles.md` |
| Self-registration | `SelfRegistrationTemplateAccessService`, `TenantMembershipService`, `UserFactory.EnsureNotificationPermissions` |
| Email | `TenantAwareEmailService`, `EmailTemplateResolver`, `EmailPersonalisationBuilder`, `EmailPlaceholderMappingProvider` |
| Files | `TenantAwareFileStorageService`, `FileValidationModeResolver`, `ApplicationFileValidationPolicy` (Domain) |
| Outbound tenant events | `EventTriggerDispatcher`, `EventMappingProvider`, `EventDataMapper`, `SchemaEventDefinitionProvider`, `MessageEndpointSelector` — see [`features/event-triggers.md`](features/event-triggers.md) |
| Notifications | `TenantScopedNotificationService`, `NotificationSignalRService`, `Api/Services/NotificationHubContext` |
| Reference numbers | `DefaultApplicationReferenceProvider` (`ApplicationReference` setting) |
| Prism | `Application/Prism/Queries/*`, `ProjectionEventPublisher`, `PrismReportingExportPolicyService` + `PrismAccessTokenSource` |

## Configuration (host `appsettings.json`)

`ConnectionStrings` (`TenantConfigDatabase`, `DefaultConnection` (dev/test), `ServiceBus`, `AzureSignalR`), `TenantConfigSource` (`Database` | `AppSettings`), `DataProtection` (`UseAzure`, `BlobUri`, `KeyVaultKeyId`, `LocalKeysPath`), `GlobalConfiguration` (`ApplicationInsights`, `FileStorage`, `Email` — host defaults exported to Web through host-config except App Insights), `Platform:AzureAd`, `Prism:ControlApi` (`BaseUrl`, `Scope`, `ManagedIdentityClientId`), `Azure:SignalR`, `MassTransit` (`Transport`, `AppPrefix`, `SubscriptionPrefix` default `extapi`, `AzureServiceBus`, `Outbox`), `CacheSettings`, `NotificationService:RedisKeyPrefix`, `RequestTimings`, `Serilog`. Never commit real values; deployed values come from Container App secrets / Key Vault.
