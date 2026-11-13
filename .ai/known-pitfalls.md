# Known pitfalls

Code-verified unless marked **Historical** / **Unverified**. Background: [`history-lessons.md`](history-lessons.md).

## Tenancy and config

1. **Global Admin GUID ≠ tenant Admin.** `RoleConstants.AdminRoleId` is the SuperAdmin shell. Assigning it as a tenant Admin breaks tenant admin assignment.
2. **Web Target overrides Shared.** Merge order is alphabetical by Target name; `Web` wins over `Shared`. Stale Web rows explain “I updated Tenant Settings but the app still fails.”
3. **One bad tenant aborts the whole config refresh.** `DatabaseTenantConfigurationProvider` flatten/JSON errors can keep a stale catalogue; new tenants look unconfigured until restart or a successful refresh. **Historical:** the old tenant duplicator copied overlapping Shared+Api keys (`InternalServiceAuth`) and crashed refresh (`TenantSettingTargetOverlapGate`). New tenant now writes only the submitted minimum rows and `TenantCreatorService` rejects overlaps.
35. **`TenantSettingJsonValidator` lets the `__SECRET__` promotion placeholder through unparsed.** That is deliberate for export/import round-trips, but any other write path that persists it stores invalid JSON and fails the catalogue refresh for every tenant (see #3). New tenant rejects it explicitly; do the same in any new write path.
4. **HostMappings ownership.** Mappings must only reference templates with `TenantId == current` or legacy `TenantId == null`. Keep the ownership validator in place.
5. **EA migrations on isolated databases.** Outbox/Prism columns missing → lost events, delivery loop errors.
6. **Unmapped hostnames are traffic noise**, not a tenant outage (`NotFoundException` for unknown hosts).

## Auth

7. **Test JWT signing keys < 16 bytes** fail with “Key size 96”.
8. **Test Auth is production-blocked.** Do not “enable it in prod TenantConfig” — the environment gate wins.
9. **Entra and Test Auth tenants still need `TokenRefresh:TokenEndpoint`.** Removing DfE Sign-In does not remove TokenRefresh validation on Web (`TenantTokenRefreshOptionsAccessor`): `CompositeAuthenticationSchemeStrategy` always builds the OIDC strategy and its token-refresh service. Symptom: `TokenEndpoint is required` in container logs — often a **Web** config issue, not API.
10. **Issuer try-all logging.** Multi-provider validation logs IDX errors for the wrong IdP even when exchange succeeds.
11. **`IsServicePrincipal` must be true** on file-validation API keys or `CanRecordFileValidation` returns 403.
30. **Notification grants are tenant-scoped.** After `ScopeNotificationPermissionsByTenant`, an existing user who joined a second tenant (self-registration or role assignment) got a membership but no `{tenantId}:{email}` Notifications grant, so `/v1/Notifications/unread` returned 403 on every page load for that tenant. Call `IUserFactory.EnsureNotificationPermissions` on every new or reactivated membership path, never for an existing active member (it would restore grants an admin removed). `BackfillTenantNotificationPermissions` repaired affected memberships without touching members that have any notification grant; cached `UserClaims_*` entries pick it up when they expire or the user is invalidated. See `features/notifications-signalr.md`.
31. **Hub endpoints must not run TenantBearer.** The SignalR client re-sends the Azure SignalR token on reconnect; `/hubs/*` is forwarded to `HubCookie` so it is never validated as a tenant JWT.
34. **Every tenant needs its own `AzureAd` setting, with a ClientId or Audience.** `POST /v1/tokens/exchange` (`ServiceCallers`) only accepts Web's Entra service token when it matches an `IsServicePrincipal` provider of the tenant in `X-Tenant-ID`, and a provider with no audiences matches nothing. A tenant without `AzureAd` (Api or Shared) cannot exchange tokens, so nobody can sign in, even though host-config and tenant-config load fine (they use `PlatformBearer`).
37. **Test Auth authenticator enrolment trusts only the JWT email or an administrator-issued setup code** (**Verified**, `Application/Security/TestAuthAuthenticatorCaller.cs`, `TestAuthAuthenticatorInviteService.cs`). The `/v1/me/test-auth-authenticator` endpoints must never take an email from the request. The anonymous `invite/redeem` endpoint may only start an enrolment after consuming a valid code for that email, and `invite/confirm` may only complete enrolments started that way (`FromInvite`). Keep the rule that only a SuperAdmin can create a code for another administrator, and keep codes audited, and the login check (`/v1/tokens/test-auth-authenticator/verify`) must keep counting attempts before it compares, even for emails with no enrolment. Enrolment saves must stay conditional on the `SetupId` the setup replaces (concurrency token on `TestAuthAuthenticators.SetupId`): an unconditional upsert let a confirmation that stalled after consuming its pending setup overwrite a newer enrolment or recreate a removed one. Removal must keep a tombstone with a new `SetupId` rather than deleting the row, or a stalled first-time setup (which expects "no enrolment") can recreate one after an enrol-then-remove. Tests that claim to cover parallel requests must make them overlap (the doubles have `HoldReadsUntil` / `HoldFindsUntil`); completed-task doubles run them one after another. Pending and saved secrets are encrypted under Data Protection purpose `TestAuthAuthenticator.v1`; losing the key ring silently un-enrols everyone (they fall back to the emailed password). See `security.md`.
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
22. **Redis is not a SignalR backplane.** Empty Azure SignalR connection string → in-process hub, broken under multiple replicas. Startup now **fails** without it unless the environment is Development, Local or code generation, or `SignalR:AllowInProcess=true` is set (`Program.AllowsInProcessSignalR`). Integration tests set the flag in `IntegrationTestHostBootstrap`.
34. **Tenant configuration is a per-replica snapshot** (**Verified**). `DatabaseTenantConfigurationProvider` holds tenants in memory and re-reads SQL every 60 seconds. Admin writes call `RefreshAsync`, which used to refresh only the replica that handled the request, so other replicas kept accepting revoked API keys, rejecting tokens signed with a rotated secret, and returning 400 for a new tenant for up to a minute. DI now hands out `ReplicatedTenantConfigurationProvider`, which refreshes locally and then publishes on `FlexFormsCacheKeys.ApiTenantConfigurationChangedChannel`; every replica subscribes and refreshes itself. The 60-second timer stays as the fallback (pub/sub does not queue). Settings edited directly in SQL still need `POST /v1/admin/tenants/refresh` or the timer. Startup code that captured `dbProvider` directly is unaffected.
35. **Read-then-write on Redis is not atomic** (**Verified**). Counters, cooldowns and single-use values must use `IAtomicCacheStore` (`Infrastructure/Caching/RedisAtomicCacheStore.cs`: SET NX, Lua INCR+PEXPIRE, Lua GET+DEL, Lua compare-and-delete), not `GetRawAsync` followed by `SetRawAsync`. The Test Authentication one-time password and hub tickets used to read then write, so parallel guesses could exceed the attempt limit and one ticket could be redeemed twice. Two further rules for anything that can be replaced while a request is in flight (the password can be re-issued after the 30-second cooldown): delete it with `DeleteIfEqualsAsync` against the exact bytes the request read, never a plain `DeleteAsync`, or a slow request can sign in with an old password and delete the new one; and give each issued value its own counter key (`{key}:attempts:{id}`) instead of resetting a shared counter after publishing, or attempts made in between are erased. Keys keep the `FlexForms:Cache:` prefix, so values written by older code stay readable (password entries without an id fall back to the old `{key}:attempts` counter).

## Contracts and packages

23. **Local CoreLibs `1.0.0.0` vs published assembly versions.**
38. **CoreLibs Contracts 2.x is net10.0 only** (**Verified**). Domain and Api.Client reference Contracts `2.0.2`, so `GovUK.Dfe.FlexForms.Api.Client` now targets net10.0 only. A consumer still on net8.0 cannot take a client release from this point.
24. **Application Insights 3.x** breaks Serilog sink / logger provider.
25. **README ER diagram still shows `TemplatePermission`.** Entity was removed; claims replaced it.
26. **Historical name External Applications** in namespaces — not a bug.

## Telemetry

28. **Host-config overrides Web's own settings.** Web merges `GET /v1/host-config?target=Web` after its appsettings and env vars, so any `GlobalConfiguration` key exported there silently wins. Previously this sent the API's App Insights connection string to Web, putting Web telemetry in the API resource. `HostConfigurationReader` now withholds `ApplicationInsights:ConnectionString` / `InstrumentationKey` for Web. Apply the same exclusion to any other key Web must own.
29. **Custom telemetry channel must forward the ingestion endpoint.** `TenantAwareTelemetryChannel` wraps an inner `ServerTelemetryChannel`. The SDK applies the connection string's `IngestionEndpoint` only to the registered (wrapper) channel, so the inner host channel posted to the legacy global endpoint and host-routed server telemetry (requests, dependencies, Serilog traces) never reached the resource. Tenant channels were unaffected because each gets its own `TelemetryConfiguration`. `Initialize` now sets the inner endpoint from `configuration.EndpointContainer.Ingestion`. It only showed up once tenant `ApplicationInsights` rows were removed and everything fell back to host. Same class and fix in flexforms-web.
33. **499 is a client abort, not a failure.** Application Insights marks every request of 400 or above as failed. `Telemetry/ClientAbortedRequestTelemetryProcessor` (registered after `AddApplicationInsightsTelemetry`) sets `Success = true` on 499 requests and keeps the code, so aborted calls stay queryable without filling the Failures blade. Same processor in flexforms-web.

## Rate limiting

27. `RateLimitingBehaviour` keys by appid/azp/email/IP + command name. Admins and internal auth bypass. Failed SignalR negotiate + tight reconnect can look like 429/WAF (**Historical**, CORS origins often the real cause).
36. **Rate limits are shared across replicas** (**Verified**). The CoreLibs `RateLimitStore` is a per-process dictionary, so N replicas meant N times each limit (including the secret-reveal break-glass limit of 5 per 10 minutes) and every deploy reset the counts. `InfrastructureServiceCollectionExtensions` replaces `IRateLimiterFactory<string>` with `RedisRateLimiterFactory`: a Lua sliding window on a sorted set under `FlexForms:RateLimit:{max}:{windowMs}:{sha256(key)}`, in the configured `CacheSettings:Redis:Database` (like every other Redis key; using the connection's default database would let deployments separated only by database share counters). If Redis is unreachable it falls back to the per-process CoreLibs limiter (limit weakened, not removed). Integration tests remove Redis, so the "with rate limiting" fixture exercises that fallback.
