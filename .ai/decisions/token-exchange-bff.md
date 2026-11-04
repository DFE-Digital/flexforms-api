# Token exchange (BFF)

- **Status:** Accepted
- **Provenance:** **Verified** in `ExchangeTokenQueryHandler` and Api.Client `TokenExchangeHandler`. Original rationale not in `docs/adrs/`.

## Context

Users authenticate at Web with DfE Sign-In or Entra. The API must authorize tenant-scoped operations with a token it can validate without depending on every tenant IdP at runtime for each API call.

## Decision

Web (and other confidential clients) call `POST /v1/tokens/exchange` with a machine credential + subject token. The API validates the subject, upserts `User` / `TenantMembership`, and issues an HS256 JWT using that tenant’s `TokenSettings`.

## Alternatives

Unknown. On-behalf-of to tenant Entra is used for some service paths (`IApiOboTokenService`) but not as the user session token.

## Consequences

- Tenant JWT `tenant_id` is the isolation claim.
- Permission claims are minted at exchange time; Web also caches permissions (`PermissionsCacheMiddleware` on Web).
- Changing claim shape requires Web and policy handler updates together.
