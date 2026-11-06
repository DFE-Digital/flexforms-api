# Feature: Notifications and SignalR

## 1. Purpose

User toasts/banners for malware, file validation, deletes. Live push to the browser.

## 2. Entry points

- `NotificationsController`, `HubAuthController`
- `Api/Hubs/NotificationHub.cs`
- CoreLibs `INotificationService` (Redis)
- `INotificationSignalRService`

## 3. Flow

Application code adds a notification → Redis store → hub `notification.upserted`. Browser gets hub ticket (cookie) then connects to `{API}/hubs/notifications`. Web `notifications.js` also polls REST for unread.

## 4. Types

CoreLibs `Notification` / `NotificationDto`. Categories include `malware-detection`, `file-validation`.

## 5. Other repos

Web: `FrontHubTicketController`, `wwwroot/js/notifications.js`. Cypress disables the hub.

## 6. Rules

Notification failure must not fail the file-validation callback (README). Tenant/user matching is in consumer code, not Service Bus SQL filters.

**Grants are per tenant** (**Verified**). `/v1/Notifications/*` needs the claim `Notifications:{email}:{Read|Write|Delete}`, built from a `Permissions` row keyed `{tenantId}:{email}` (`TenantScopedIdentityKey`, `TenantPermissionFilter`). Every path that gives an **existing** user a new tenant membership must call `IUserFactory.EnsureNotificationPermissions(user, tenantId, …)`: self-registration (`RegisterUserCommandHandler`), role assignment (`AssignUserRoleCommandHandler`) and contributor invites (`AddContributorCommandHandler`, inline). Missing it makes every Web page load return 403 from `/notifications/unread`. Migration `20261106100000_BackfillTenantNotificationPermissions` grants it to every active membership that lacked it.

**Hub auth is cookie-only** (**Verified**). `ForwardDefaultSelector` in `Security/AuthorizationExtensions.cs` sends `/hubs/*` to the `HubCookie` scheme. On reconnect the SignalR JS client re-sends the Azure SignalR access token (issuer `azure-signalr`) as a bearer; letting `TenantBearer` validate it only produced `SecurityTokenInvalidIssuerException` / `SecurityTokenExpiredException` noise. Hub tickets (`auth/hub-ticket`) are single use and live 60 seconds; the cookie lives 10 minutes with no sliding.

## 7. Edge cases

In-process SignalR + multiple replicas. Shared Azure SignalR across environments needs distinct `ApplicationName`. CORS on negotiate if `FrontendOrigins` empty/wrong → retry storms.

`GET auth/hub-cookie` 401 means the ticket was missing: expired (> 60s), already redeemed, or minted under another tenant. Hidden browser tabs throttle timers, so a renewal can redeem too late; Web now skips renewal while hidden and renews on `visibilitychange` / `onreconnecting`. Negotiate 401 after hours idle is the 10-minute cookie having expired.

## 8. Tests

Notification handler tests, hub integration tests.

## 9. Limits

Not a general pub/sub for tenant integrations. Those use EventMappings / Service Bus topics.
