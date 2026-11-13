# Test Authentication authenticator app enrolments live in TenantConfig

- **Status:** Accepted
- **Provenance:** **Verified** — `TestAuthAuthenticatorService`, `TestAuthAuthenticatorStore`, `TenantConfigDbContext`, migration `AddTestAuthAuthenticators`; Web `TestLogin`, `TestAuthenticator` pages.

## Context

Test Authentication signs users in with a one-time password emailed through GOV.UK Notify. Some testers cannot receive email reliably, so they need a second factor that does not depend on email. Test Authentication identifies users by email and tenant before any token exchange, so there may be no EA `User` row yet, and one EA database can serve several tenants.

## Decision

- Users can enrol an authenticator app (TOTP) after signing in once with the emailed password, or with a one-time setup code from an administrator. Enrolment is per tenant and per email, taken from the signed-in user's JWT or from the setup code, never from the request alone.
- Enrolments are stored in a new `tenantconfig.TestAuthAuthenticators` table rather than on `User` or `TenantMembership` in the EA database: a column on `User` would apply across every tenant sharing that EA database, and an EA migration would have to reach every tenant EA database. One TenantConfig migration is enough, and the table sits next to the tenant setting that switches Test Authentication on.
- Secrets are encrypted with the shared Data Protection key ring under purpose `TestAuthAuthenticator.v1`, both at rest and while pending in Redis.
- Email OTP is always still offered on the sign-in page; the authenticator is an alternative, not a replacement.
- New request/response types were added to CoreLibs Contracts (2.0.1), which is net10.0 only, so Api.Client dropped net8.0.

## Consequences

- Personal data (email addresses) now lives in the TenantConfig database. Deleting a user in the EA database does not remove their enrolment row.
- Users who cannot receive the first email get a one-time setup code from an administrator instead (added later, same table and Contracts 2.0.2). Codes live only in Redis as hashes, are audited in `TenantAccessAudits`, and only a SuperAdmin can create one for another administrator. A code also replaces a lost device's enrolment.
- Losing the Data Protection key ring un-enrols everyone; they fall back to email.
- Releases must go CoreLibs Contracts, then API (migration runs with the TenantConfig bundle) and Api.Client, then Web.
