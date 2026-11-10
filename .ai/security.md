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
- OTP generate/verify is **API-owned** (Redis TTL + Notify). Web mints the test JWT after verify. Cooldown, attempt count and consumption go through `IAtomicCacheStore`; never move them back to read-then-write.
- `TenantSettingsReader` merges Shared then target; **`Web` sorts after `Shared` and wins**. Editing only Shared leaves a stale Web row in control.
- OTP endpoints must remain behind `ServiceCallers` + environment gate. Fail closed in Production.

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
