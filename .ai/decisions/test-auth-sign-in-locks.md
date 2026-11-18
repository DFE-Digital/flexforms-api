# Test Authentication administrator sign-in locks

- **Status:** Accepted
- **Provenance:** **Verified** — `TestAuthSignInLockService`, `TestAuthSignInLockStore`, `TestAuthSignInLockAdminGuard`, migration `AddTestAuthSignInLocks`, the Test Auth sign-in handlers in `Users/Commands`; Web `TestLogin`, `Admin/UserManagerEdit`.

## Context

Test Authentication only had a temporary lockout: five authenticator app codes without success lock that email out of authenticator sign-in until 15 minutes after the first attempt. Administrators could not clear it early, could not lock someone out on purpose, and a locked user saw the same "incorrect code" message as for a wrong code. The emailed one-time password was never locked, so it was always a way back in.

## Decision

- An administrator lock blocks **every** Test Authentication sign-in method for that tenant and email (send and verify emailed password, authenticator verify, setup code redeem and confirm). A lock on the authenticator alone would not lock anyone out.
- Locks are stored in their own `tenantconfig.TestAuthSignInLocks` table, keyed by tenant and normalised email like `TestAuthAuthenticators`, and not as a column on that table: the authenticator row is rewritten (and tombstoned) by enrolment, which must never clear a lock. TenantConfig rather than EA for the same reasons as enrolments (`test-auth-authenticator-app.md`).
- The lock never expires. Unlocking also clears the temporary lockout. The temporary lockout stays in Redis and is shown to administrators separately, with its own "Unlock now" action, so saving a lock form can never turn a temporary lockout into a permanent one.
- Lock changes have their own endpoints and their own form on User Manager Edit, apart from role and form access, so one cannot leave the other half saved.
- Locked sign-in returns lockout details in `VerifyTestAuthPasswordResponse` (`IsLockedOut`, `IsPermanentLockout`, `RetryAfterSeconds`, Contracts 2.0.6) or 423 Locked where the endpoint has no body (send, redeem), so Web can tell the user how long to wait or to contact their tenant administrator.
- Same permission rules and audit as setup codes.

## Consequences

- Another table holding email addresses in TenantConfig. Rows are deleted on unlock; they are not removed when an EA user is deleted.
- A lock does not end an existing session.
- Releases must go CoreLibs Contracts 2.0.6, then API (migration runs with the TenantConfig bundle) and Api.Client, then Web.
