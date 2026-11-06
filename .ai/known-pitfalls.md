# Known pitfalls

Code-verified unless marked **Historical** / **Unverified**. Background: [`history-lessons.md`](history-lessons.md).

## Tenancy and config

1. **Global Admin GUID ≠ tenant Admin.** `RoleConstants.AdminRoleId` is the SuperAdmin shell. Assigning it as a tenant Admin breaks tenant admin assignment.
2. **Web Target overrides Shared.** Merge order is alphabetical by Target name; `Web` wins over `Shared`. Stale Web rows explain “I updated Tenant Settings but the app still fails.”
3. **One bad tenant aborts the whole config refresh.** `DatabaseTenantConfigurationProvider` flatten/JSON errors can keep a stale catalogue; new tenants look unconfigured until restart or a successful refresh. `TenantDuplicator` historically copied overlapping Shared+Api keys (`InternalServiceAuth`) and crashed refresh (`TenantSettingTargetOverlapGate`).
4. **HostMappings ownership.** Mappings must only reference templates with `TenantId == current` or legacy `TenantId == null`. Keep the ownership validator in place.
5. **EA migrations on isolated databases.** Outbox/Prism columns missing → lost events, delivery loop errors.
6. **Unmapped hostnames are traffic noise**, not a tenant outage (`NotFoundException` for unknown hosts).

## Auth

7. **Test JWT signing keys < 16 bytes** fail with “Key size 96”.
8. **Test Auth is production-blocked.** Do not “enable it in prod TenantConfig” — the environment gate wins.
9. **Entra tenants still need `TokenRefresh:TokenEndpoint`.** Removing DfE Sign-In does not remove TokenRefresh validation on Web (`TenantTokenRefreshOptionsAccessor`). Symptom: `TokenEndpoint is required` in container logs — often a **Web** config issue, not API.
10. **Issuer try-all logging.** Multi-provider validation logs IDX errors for the wrong IdP even when exchange succeeds.
11. **`IsServicePrincipal` must be true** on file-validation API keys or `CanRecordFileValidation` returns 403.
30. **Notification grants are tenant-scoped.** After `ScopeNotificationPermissionsByTenant`, an existing user who joined a second tenant (self-registration or role assignment) got a membership but no `{tenantId}:{email}` Notifications grant, so `/v1/Notifications/unread` returned 403 on every page load for that tenant. Call `IUserFactory.EnsureNotificationPermissions` on every new or reactivated membership path, never for an existing active member (it would restore grants an admin removed). `BackfillTenantNotificationPermissions` repaired affected memberships without touching members that have any notification grant; cached `UserClaims_*` entries pick it up when they expire or the user is invalidated. See `features/notifications-signalr.md`.
31. **Hub endpoints must not run TenantBearer.** The SignalR client re-sends the Azure SignalR token on reconnect; `/hubs/*` is forwarded to `HubCookie` so it is never validated as a tenant JWT.
32. **Api.Client `TokenExchangeHandler` and client aborts.** It catches every exception and returns a synthetic 401. When the browser aborts (`HttpContext.RequestAborted`), it now logs a warning (no exception object) and throws `OperationCanceledException`, so Web records a 499 instead of an Error plus a fake 401. Ship it by bumping `GovUK.Dfe.FlexForms.Api.Client` in Web.

## Data Protection

12. **`SetApplicationName` on local ring** invalidates ciphertext encrypted with default identity + purpose `TenantSettings.v1`.
13. **`%LOCALAPPDATA%` is literal** in configuration.
14. **Windows DPAPI keys ≠ Linux container keys.**

## Messaging / Prism / files

15. **Prism events must publish before `SaveChanges`.** Post-commit domain-event publishes need a follow-up save to reach the outbox; Prism uses `beforeCommit` / pre-commit publish instead.
16. **Default outbox allowlist is empty.** Only Prism events are forced onto the outbox. Listing `ScanRequestedEvent` requires duplicate-tolerant consumers or topic duplicate detection (set at topic **creation**).
17. **`RequirePassed` has no timeout escape hatch.** Tenant function downtime blocks submit.
18. **Both API and Web consume `file-scanner-results`.** Removing one breaks cleanup or UX.
19. **Do not log SAS URLs.**

## Cache / Redis

20. **Tenant prefix `t:{guid}:` is mandatory.** Accepting another tenant’s prefixed key bypasses isolation.
21. **API cache can degrade to SQL; Web session cannot.** Same Redis incident looks like “API healthy, site dead.”
22. **Redis is not a SignalR backplane.** Empty Azure SignalR connection string → in-process hub, broken under multiple replicas.

## Contracts and packages

23. **Local CoreLibs `1.0.0.0` vs published assembly versions.**
24. **Application Insights 3.x** breaks Serilog sink / logger provider.
25. **README ER diagram still shows `TemplatePermission`.** Entity was removed; claims replaced it.
26. **Historical name External Applications** in namespaces — not a bug.

## Telemetry

28. **Host-config overrides Web's own settings.** Web merges `GET /v1/host-config?target=Web` after its appsettings and env vars, so any `GlobalConfiguration` key exported there silently wins. Previously this sent the API's App Insights connection string to Web, putting Web telemetry in the API resource. `HostConfigurationReader` now withholds `ApplicationInsights:ConnectionString` / `InstrumentationKey` for Web. Apply the same exclusion to any other key Web must own.
29. **Custom telemetry channel must forward the ingestion endpoint.** `TenantAwareTelemetryChannel` wraps an inner `ServerTelemetryChannel`. The SDK applies the connection string's `IngestionEndpoint` only to the registered (wrapper) channel, so the inner host channel posted to the legacy global endpoint and host-routed server telemetry (requests, dependencies, Serilog traces) never reached the resource. Tenant channels were unaffected because each gets its own `TelemetryConfiguration`. `Initialize` now sets the inner endpoint from `configuration.EndpointContainer.Ingestion`. It only showed up once tenant `ApplicationInsights` rows were removed and everything fell back to host. Same class and fix in flexforms-web.

## Rate limiting

27. `RateLimitingBehaviour` keys by appid/azp/email/IP + command name. Admins and internal auth bypass. Failed SignalR negotiate + tight reconnect can look like 429/WAF (**Historical**, CORS origins often the real cause).
