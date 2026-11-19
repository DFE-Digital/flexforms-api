# Feature: Token exchange

## 1. Purpose

Turn an IdP token into a tenant-scoped API JWT with role and permission claims.

## 2. Entry points

- `Api/Controllers/TokensController.cs`
- `Application/Users/Queries/ExchangeTokenQueryHandler.cs`
- Api.Client: `Security/TokenExchangeHandler.cs`, `TokenAcquisitionService.cs`

## 3. Flow

Web OIDC cookie → subject token → `POST /v1/tokens/exchange` (ServiceCallers) → validate via CoreLibs `IExternalIdentityValidator` (tenant OIDC registry, test HMAC, or internal headers) → upsert user/membership → mint JWT.

## 4. Key types

`ExchangeTokenQuery`, `ITenantMembershipService`, `ISelfRegistrationTemplateAccessService`, `IUserTokenServiceFactory`.

## 5. Other repos

Web: `RequestTokenExchange` forced when platform bootstrap is on. CoreLibs: `AddExternalIdentityValidation`, test auth, Entra SSO.

## 6. Rules

Production blocks Test Auth. Self-registration template grants as in business-rules. SuperAdmin elevation when `Users.RoleId` is `RoleConstants.AdminRoleId`.

## 7. Edge cases

- Short test signing keys.
- Web Target overriding Shared TestAuthentication.
- Multi-provider IDX log noise.
- SuperAdmin elevation and Test Auth paths — re-read the handler and its tests before changing who may exchange.
- No active membership (and not platform SuperAdmin) → `Forbid` "User is not a member of tenant '…'". Keep the phrase `TokenExchangeErrors.NotTenantMemberMarker` ("not a member of tenant"): Api.Client `UserAutoRegistrationHandler` uses it to auto-register, and `TokenExchangeHandler` uses it to return a 403 with `ErrorId` `TokenExchangeErrors.NoTenantMembershipErrorId` (`Exchange-403-NoTenantMembership`) and a user-facing message, instead of the synthetic 401. That path does not set the forced-logout flag. Web shows the reason on its Forbidden page. Deployed clients match the phrase, so changing the API wording breaks them; `ExchangeTokenQueryHandlerTests` guards it.

## 8. Tests

`ExchangeTokenQueryHandlerTests`, TokensController integration tests.

## 9. Limits

Permission claims are a snapshot at exchange. Web refreshes via `PermissionsCacheMiddleware`. Idle/absolute timeouts are **Web** TokenRefresh, not this handler.
