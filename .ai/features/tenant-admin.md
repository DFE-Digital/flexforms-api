# Feature: Tenant admin and settings

## 1. Purpose

CRUD tenant settings, secrets, import/export, create a new tenant, refresh catalogue.

## 2. Entry points

- `TenantAdminController`
- `UpsertTenantSettingCommandHandler`, `TenantSettingJsonValidator`
- `ITenantSettingsEncryptor`, `TenantSettingSecretPlaintextGate`
- New tenant: `CreateTenantCommandHandler`, `NewTenantSettingsRequirements`, `ITenantCreator` / `TenantCreatorService`

## 3. Flow

Admin UI (Web) → API upsert with Target Shared/Api/Web. Secrets encrypted. List/validate redact. Production SuperAdmin reveal is audited.

New tenant (**Verified**): Web `/admin/new-tenant` prefills the minimum rows from the caller's tenant, the SuperAdmin edits them, and Web posts them inside Base64 `payloadJson` to `POST /v1/admin/tenants/{ownTenantId}/clone` (route and `CloneTenantRequest` kept for wire compatibility; it no longer clones). The handler only writes the submitted rows. `__REDACTED__` leaves are restored from the caller's tenant row with the same Category + Target. Anything outside `NewTenantSettingsRequirements.Categories` is rejected, and the result must pass `NewTenantSettingsRequirements.Validate`. `Template` / `ApplicationTemplates` rows with template ids go through `TemplateHostMappingOwnershipValidator` for the **new** tenant id, so copied GUIDs owned by the current tenant are rejected. That validator reads the current request's EA database, so mappings are only accepted when the new tenant's `DefaultConnection` equals the current tenant's; otherwise they are rejected and must be added in Tenant Settings after creation. A bare `__SECRET__` (or `{"__SECRET__":true}`) promotion placeholder is rejected, and so are `null` entries in `settings`. Templates, principals, Layout, InternalServiceAuth and anything else not listed are added afterwards in Tenant Settings.

Optional copied categories (`NewTenantSettingsRequirements.OptionalCategories`, **Verified**): FeatureManagement, FileStorage, Template, ApplicationTemplates, NotificationService, NotificationBanner, MassTransit, FrontendSettings, FormEngine, ExternalApplicationsApiClient, CacheSettings, ApplicationTerminology, ApplicationReference. Web also copies DfESignIn, EntraSso and Email when the current tenant has them. These are not needed to run, and `Validate` does not check them; Web copies them as-is (no placeholders) because they are usually configured before go-live. Keeping `__REDACTED__` in them reuses the current tenant's secrets (Notify key, storage account, client secrets).

### Minimum settings to run a tenant (**Verified**)

| Category | Needed by | Why |
|----------|-----------|-----|
| `ConnectionStrings:DefaultConnection` (Api/Shared) | API | `ExternalApplicationsContext` throws for a tenant without it |
| `Authorization:TokenSettings` SecretKey/Issuer/Audience (Api/Shared) | API | Token exchange signs and validates user JWTs (`TenantTokenSettingsConfigurator`) |
| `AzureAd` TenantId or Issuer, **and** ClientId or Audience (Api/Shared) | API | `ServiceCallers` only accepts Web's Entra service token if it matches a service-principal provider of the called tenant (`DatabaseTenantAuthProviderRegistry`); a provider with no audiences matches nothing |
| `Authentication:Scheme` (Web/Shared) | Web | Chooses the sign-in |
| Provider for the scheme, Web view: `DfESignIn` Authority/ClientId/ClientSecret, or `EntraSso` TenantId/ClientId | Web | Web signs the user in |
| Provider for the scheme, Api view: `DfESignIn` **DiscoveryEndpoint** + ClientId + Authority/Issuer, or `EntraSso` **Enabled true** + TenantId + ClientId | API | `TenantOidcProviderBuilder` skips DfE Sign-in without an explicit DiscoveryEndpoint (it is not derived from Authority) and Entra SSO unless enabled with a TenantId; ClientId is the audience |
| Or `TestAuthentication` (Api and Web, same values) | Web + API | Web mints the test JWT; the API validates it |
| `TokenRefresh`, or a `DfESignIn` Authority/ClientId/ClientSecret (Web/Shared) | Web | `TenantTokenRefreshOptionsAccessor` validates on every request for every scheme |
| Test Authentication only: `Email` (Provider + Notify ApiKey) and `EmailTemplates` with `TestAuthPasswordEmail` (Api/Shared) | API | Test sign-in emails a one-time password |

Plus one hostname and one frontend origin. Empty `ApplicationTemplates`/`Template` HostMappings are **not** needed: the catalogue only adds tenant-owned templates when mappings are missing, and Web never falls back to host mappings for a tenant request.

## 4. Entities

`TenantSettingEntity`, audits, principals.

## 5. Other repos

Web Tenant Settings page (masking is UX only) and New tenant page (`NewTenantAdminService.BuildDraft` must stay in step with `NewTenantSettingsRequirements`). Scripts in `scripts/` for bootstrap.

## 6. Rules

SuperAdmin-only categories. Sentinel `__REDACTED__` restore. Overlap gate: same flattened keys must not exist on Shared and Api/Web. New tenant: interactive SuperAdmin of the source tenant only; Test Authentication cannot be enabled in Production.

## 7. Edge cases

Refresh fail-closed. Reveal is break-glass, not a substitute for RBAC. New tenant reuses the source tenant's secrets wherever the SuperAdmin leaves `__REDACTED__` (for example a shared EA connection string); token signing secrets are generated fresh by Web.

## 8. Tests

`TenantSettingSecretPlaintextGateTests`, `TenantSettingTargetOverlapGateTests`, `CreateTenantCommandHandlerTests`, `NewTenantSettingsRequirementsTests`, `TenantCreatorServiceTests`, TenantAdmin handler tests.

## 9. Limits

Settings changes reach running instances on the next refresh, not immediately.
