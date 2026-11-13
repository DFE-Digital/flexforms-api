# Security

**Verified** in `Api/Security/AuthorizationExtensions.cs`, `Application/Users/Queries/ExchangeTokenQueryHandler.cs`, and [`README.md`](../README.md).

## Authentication schemes

| Scheme | Use |
|--------|-----|
| `CompositeScheme` | Default dispatcher |
| `TenantBearer` | User JWT (per-tenant `TokenSettings` HS256) + Entra service tokens |
| `ApiKey` | `X-Api-Key` hashed with `TenantApiKeyHasher` against `AuthProviders` |
| `Mtls` | Client cert thumbprint |
| `PlatformBearer` | Platform Entra app (`Platform:AzureAd`) — host-config, tenant-config consume, Prism |
| `HubCookie` | Short-lived cookie for SignalR. `CompositeScheme` forwards every `/hubs/*` request here, so hub traffic never runs `TenantBearer` |

Internal Prism endpoints (`InternalPrismController`) require **platform** app role **`Prism.Read`** (`PlatformPrismRead`). User JWTs, API keys, and tenant Entra tokens are rejected. **Historical:** putting `Prism.Read` on a tenant API app instead of the platform app causes 403.

## Token exchange

`POST /v1/tokens/exchange` (policy `ServiceCallers`):

1. Caller presents machine credential + subject IdP token (DfE Sign-In / Entra SSO / test / internal).
2. `IExternalIdentityValidator.ValidateIdTokenAsync` (CoreLibs). Production **hard-blocks** Test Authentication (`TestAuthenticationEnvironmentGate`).
3. Find or create `User`, ensure `TenantMembership`.
4. Issue tenant-scoped user JWT with role + permission claims.

Web’s Api.Client uses this on every user session (`RequestTokenExchange`).

Changes to who may exchange (memberships, SuperAdmin elevation, Test Auth) must be tested against every IdP path, including Test Auth, before merging.

Issuer mismatches: CoreLibs may try multiple OIDC providers; wrong-provider attempts log IDX errors even when auth later succeeds. Treat log volume as noise unless the request actually fails.

## Test authentication / OTP

- Signing key must be ≥16 bytes (32+ recommended). Short keys throw `SymmetricSecurityKey` size errors.
- OTP generate/verify is **API-owned** (Redis TTL + Notify). Web mints the test JWT after verify. Cooldown, attempt count and consumption go through `IAtomicCacheStore`; never move them back to read-then-write. Each password has its own id and attempt counter, and is consumed or invalidated only by compare-and-delete against the entry the request read (`.ai/known-pitfalls.md` item 35).
- `TenantSettingsReader` merges Shared then target; **`Web` sorts after `Shared` and wins**. Editing only Shared leaves a stale Web row in control.
- OTP endpoints must remain behind `ServiceCallers` + environment gate. Fail closed in Production.

### Authenticator app (TOTP) alternative

**Verified** in `Application/Security/TestAuthAuthenticatorService.cs`, `Infrastructure/Services/TestAuthAuthenticatorStore.cs`, `TokensController`, `UsersController`.

A user who has enrolled an authenticator app for their email **in this tenant** can sign in with its code instead of the emailed password. The emailed password always stays available.

| Endpoint | Auth | Purpose |
|----------|------|---------|
| `POST /v1/tokens/test-auth-authenticator/verify` | `ServiceCallers` + environment gate | Login check (`VerifyTestAuthAuthenticatorCodeRequest` → `VerifyTestAuthPasswordResponse`) |
| `GET /v1/me/test-auth-authenticator` | User JWT (`[Authorize]`) | Enrolment status |
| `POST /v1/me/test-auth-authenticator/setup` | User JWT, `[RateLimit(10, 60)]` | New secret held as a pending enrolment in Redis for 10 minutes; returns key + `otpauth://` URI |
| `POST /v1/me/test-auth-authenticator/setup/confirm` | User JWT, `[RateLimit(10, 60)]` | Saves the pending secret when the code matches |
| `DELETE /v1/me/test-auth-authenticator` | User JWT | Removes the caller's enrolment |

- Enrolment endpoints take the email **only from the user JWT** (`TestAuthAuthenticatorCaller`), never from the request, and apply the same environment gate + `TestAuthentication:Enabled` check. Never add an email parameter to them.
- Enrolments live in `tenantconfig.TestAuthAuthenticators` (unique per `TenantId` + normalised email). Secrets, including the pending one in Redis, are encrypted with Data Protection purpose `TestAuthAuthenticator.v1` (`DataProtectionTestAuthAuthenticatorSecretProtector`). An undecryptable secret is treated as not enrolled; the user signs in by email and enrols again.
- TOTP: SHA1, 6 digits, 30 second step, ±1 step drift (Otp.NET). Starting setup again does not disable the current enrolment until a new code is confirmed.
- The login check counts every attempt before comparing, including for emails with no enrolment, so the response does not reveal who has enrolled. 5 attempts within 15 minutes lock that email out of authenticator sign-in (email OTP still works). Each code is accepted once (SET NX per time step). Same `IAtomicCacheStore` rules as the OTP (`.ai/known-pitfalls.md` item 35).
- Rows are not removed when an EA user is deleted; delete them by tenant + email if needed.

### Administrator-issued setup codes

**Verified** in `Application/Security/TestAuthAuthenticatorInviteService.cs`, `CreateTestAuthAuthenticatorInviteCommandHandler`, `RedeemTestAuthAuthenticatorInviteCommandHandler`, `ConfirmTestAuthAuthenticatorInviteSetupCommandHandler`.

For users who cannot receive the first email, an administrator creates a one-time setup code and gives it to them outside the system.

| Endpoint | Auth | Purpose |
|----------|------|---------|
| `POST /v1/users/test-auth-authenticator-invites` | `CanManageUsers`, `[RateLimit(10, 60)]` | Creates a code for an email (the user need not exist). Returned once |
| `POST /v1/tokens/test-auth-authenticator/invite/redeem` | `ServiceCallers` + environment gate | Consumes the code and starts a pending enrolment (`BeginTestAuthAuthenticatorSetupResponse`) |
| `POST /v1/tokens/test-auth-authenticator/invite/confirm` | `ServiceCallers` + environment gate | Saves the enrolment when the app code matches (`VerifyTestAuthPasswordResponse`); Web then signs the user in |

- Whoever holds a code can enrol an app for that email and so sign in as them. Only a SuperAdmin (`IsInteractivePlatformAdmin`) can create a code for another administrator (tenant `Admin` membership or platform SuperAdmin); anyone with `CanManageUsers` can create one for an ordinary user or themselves. Every code is written to `TenantAccessAudits` (action `TestAuthAuthenticatorSetupCodeCreated`, actor + subject email).
- Codes: 10 characters from an alphabet without 0/O/1/I, shown as `XXXXX-XXXXX`; case, spaces and hyphens are ignored. Only a SHA-256 hash is stored in Redis (tenant-scoped, per email), 24 hour expiry, single use (compare-and-delete), 5 wrong attempts discard it, and a new code replaces an unused one.
- The anonymous confirm only completes a pending enrolment that was started from a code (`PendingSetupEntry.FromInvite`), and checks that before counting attempts, so it cannot touch a signed-in user's own pending setup.
- Confirming replaces any existing enrolment, so a setup code is also how an administrator helps a user who lost their device.
- Codes are tenant-scoped, so Web can only create them for the admin's own tenant. For another tenant, someone with Redis write access can create one by hand: [`docs/test-auth-authenticator-setup-codes.md`](../docs/test-auth-authenticator-setup-codes.md). Keep that runbook in step with the key and value format in `TestAuthAuthenticatorInviteService`. Never insert `TestAuthAuthenticators` rows by hand (the secret must be Data Protection ciphertext).

## Rate limits and revocation across replicas

- `[RateLimit]` commands and the secret-reveal limit use `RedisRateLimiterFactory`, so the configured limit holds across all replicas (`.ai/known-pitfalls.md` item 36).
- Revoking credentials through the admin endpoints (API keys, mTLS thumbprints, Test Authentication, tenant deactivation) takes effect on every replica immediately via the tenant configuration broadcast (item 34). A direct SQL edit waits for the 60-second refresh.

## Tenant consistency

Bearer `tenant_id` must match the resolved tenant. Never weaken this check.

## Secrets and Data Protection

- Encryptor: `DataProtectionTenantSettingsEncryptor`, purpose `TenantSettings.v1` (`Infrastructure/Services/DataProtectionTenantSettingsEncryptor.cs`).
- Local vs Azure rings: `TenantSettingsDataProtectionExtensions`. **Do not** force `SetApplicationName` on the **local** ring if existing ciphertext was protected with the default identity — that is a purpose-string mismatch, not a missing key.
- Windows DPAPI keys are unreadable in Linux containers. Docker needs its own ring or Azure blob keys.
- `%LOCALAPPDATA%` in config is **not** expanded. Use a real path or Azure.

## File-validation callback auth

Entra app registered as a FlexForms User (`ExternalProviderId` = appid) with `FileValidation:*:Write`, **or** API key with `IsServicePrincipal: true` and `Roles: ["FileValidation"]`. Store **SHA-256 hex** of the key in TenantConfig, never the raw key.

## Logging

Do not log SAS URLs or file access tokens. Prefer names/ids.

## Headers

Security headers are configured in `Program.cs`.

## Expected security tests

Golden file: `src/Tests/GovUK.Dfe.FlexForms.Api.Tests/Security/ExpectedSecurity.json`. CI fails if a controller action’s policy drifts.
