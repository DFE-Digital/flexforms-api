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

## 7. Edge cases

In-process SignalR + multiple replicas. Shared Azure SignalR across environments needs distinct `ApplicationName`. CORS on negotiate if `FrontendOrigins` empty/wrong → retry storms.

## 8. Tests

Notification handler tests, hub integration tests.

## 9. Limits

Not a general pub/sub for tenant integrations. Those use EventMappings / Service Bus topics.
