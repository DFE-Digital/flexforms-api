# Transactional outbox (MassTransit)

This guide is for developers, platform engineers and administrators who need to understand, configure, roll out or support the transactional outbox in the FlexForms API.

- [What it is](#what-it-is)
- [Why we use it](#why-we-use-it)
- [How it works in FlexForms](#how-it-works-in-flexforms)
- [Which events use it](#which-events-use-it)
- [Configuration reference](#configuration-reference)
- [Configuration examples](#configuration-examples)
- [Event identifiers you can list](#event-identifiers-you-can-list)
- [Choosing which events to put through the outbox](#choosing-which-events-to-put-through-the-outbox)
- [Database setup and migrations](#database-setup-and-migrations)
- [Rollout runbook](#rollout-runbook)
- [Turning it off safely](#turning-it-off-safely)
- [Monitoring](#monitoring)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)
- [Code map](#code-map)

---

## What it is

When the API saves something and needs to tell other systems about it (for example "scan this uploaded file"), it has to do two things against two different systems:

1. Save the data in SQL.
2. Send a message to Azure Service Bus.

These cannot be wrapped in a single transaction, so one can succeed while the other fails. Without an outbox:

- **Saved but not sent**: Service Bus is down, so the file is stored but never scanned. The failure is only logged.
- **Sent but not saved**: a message goes out for data that was then rolled back.

The **transactional outbox** fixes this. Instead of sending the message immediately, the API writes it to an outbox table **in the same database**, as part of the same save. A background delivery service then reads the outbox and sends the messages to Service Bus, retrying until it succeeds.

Think of it as an office out-tray: you file the paperwork and drop the letter in the tray at the same time; a clerk takes letters from the tray to the post office and only removes each one once it has been posted.

## Why we use it

| Without outbox | With outbox |
|---|---|
| A Service Bus outage loses messages (logged only) | Messages wait in SQL and are delivered when the bus recovers |
| At-most-once delivery | At-least-once delivery |
| No record of what was sent | Pending messages are visible in `ea.OutboxMessage` |
| No duplicates from the publisher | Rare duplicates are possible (see below) |

It is required for **Prism** (analytics projection), where every save must reliably produce an event, otherwise analytics silently drift from the source data.

### The trade-off: duplicates

The outbox guarantees a message is sent **at least once**. In a rare failure (the message is sent to Service Bus, then the API crashes or loses its database connection before it records the message as sent) the message is sent again on recovery.

Some existing subscribers cannot handle duplicates. That is why the outbox is enabled **per event** through an allowlist, and why the default list is empty.

## How it works in FlexForms

```mermaid
sequenceDiagram
    participant API as API request
    participant DB as Tenant EA database
    participant Loop as Outbox delivery loop (per tenant DB)
    participant SB as Azure Service Bus

    API->>DB: SaveChanges (business data)
    Note over API: Domain event handlers run after commit
    API->>API: Handler publishes event
    alt Event is on the outbox allowlist
        API->>DB: Follow-up save writes ea.OutboxState + ea.OutboxMessage
        API-->>Loop: Wake-up signal
        Loop->>DB: Lock and read pending outbox
        Loop->>SB: Send message (same MessageId, headers)
        Loop->>DB: Remove delivered rows
    else Event is not on the allowlist
        API->>SB: Publish directly (previous behaviour)
    end
```

Key points:

- **Tenant-aware storage**: outbox rows are written to the EA database of the tenant that raised the event, alongside that tenant's data.
- **Tenant-aware delivery**: MassTransit's built-in delivery service has no tenant context and would only drain the first tenant's database, so it is disabled. `TenantOutboxDeliveryService` runs one MassTransit delivery loop **per distinct tenant EA database**, with that tenant's context set. Tenants that share a database share a loop.
- **Tenant headers are preserved**: `TenantId`, `TenantName` and all custom headers (`serviceName`, `PartitionKey`, `SessionId`, `Label`, and so on) are stored with the message and sent unchanged. Subscribers and `TenantContextConsumeFilter` see exactly what they saw before.
- **Tenant changes are picked up automatically**: when tenant configuration is refreshed, loops start for new databases and stop for removed ones. No restart needed.
- **Post-commit handlers are covered**: our domain event handlers run after the database commit. `DomainEventDispatcherInterceptor` performs a follow-up save so messages they publish reach the outbox tables. If that follow-up save fails, the error is logged and the original request still succeeds, which matches the existing rule that messaging problems must not fail a committed request.
- **Consumers are unaffected**: messages published inside a MassTransit consumer (for example `ScanResultConsumer`) use the consume context and go straight to the bus, as before.
- **Delivery latency**: normally milliseconds (a wake-up signal fires when the request finishes). If a signal is missed, loops poll every `QueryDelay` (5 seconds by default).
- **Multiple API instances are safe**: every instance runs delivery loops; MassTransit uses SQL row locks so each outbox is delivered by one instance at a time.

> **Atomicity note:** today's events are raised by handlers that run after the business commit, so their outbox rows are saved in a follow-up save. They are **durable and retried**, but not strictly atomic with the business change. New events that need a strict guarantee (such as Prism's) must be published **before** `SaveChangesAsync` so they are saved in the same transaction.

## Which events use it

The outbox is **only** used when all of the following are true:

1. MassTransit is running (`SkipMassTransit` is `false`).
2. `MassTransit:Outbox:Enabled` is `true`.
3. The event matches the routing rule: `Mode` is `All`, **or** `Mode` is `Allowlist` and the event is listed in `Events`, **or** it is a Prism event (see below).
4. The event is published outside a consumer (API requests, domain event handlers).

**Prism events always use the outbox.** `ApplicationProjectionRequestedEvent` and `TemplateVersionPublishedEvent`
are hard-coded in `MessageEndpointSelector` to use the outbox whatever `Mode` and `Events` say, because Prism
depends on them being atomic with the change. It is published **before** the save commits, so the outbox row, the data change and the
`SourceRevision` increment share one transaction. It still needs `Enabled` to be `true`: with the outbox switched
off it would publish directly and lose that guarantee. It goes to topic `flexforms-prism`, which must have
duplicate detection on, with session ID `{tenantId}:{applicationId}` and a deterministic `MessageId`.

Everything else is published directly to Service Bus, exactly as before the outbox existed.

## Configuration reference

All settings live under `MassTransit:Outbox` in host configuration (`appsettings*.json`, environment variables or App Service settings). A tenant can override the routing settings (`Enabled`, `Mode`, `Events`) for its own events in TenantConfig. See [Per-tenant overrides](#per-tenant-overrides). `Delivery` settings are host-only.

### Routing settings

| Setting | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `true` | Master switch. `false` removes the outbox completely: no outbox registration, no delivery loops, and every event publishes directly. Changing it needs an app restart. |
| `Mode` | `Allowlist` or `All` | `Allowlist` | `Allowlist`: only events listed in `Events` use the outbox. `All`: every event published outside a consumer uses the outbox, and `Events` is ignored. |
| `Events` | string array | `[]` | Event identifiers routed through the outbox in `Allowlist` mode. Matching ignores case. See [Event identifiers you can list](#event-identifiers-you-can-list). An empty list means no events use the outbox. |

Notes:

- The effective `Mode` and `Events` are logged at startup: `Transactional outbox routing: Mode Allowlist, Events [...]`.
- A misspelt identifier does not cause an error; the event simply publishes directly. Check the startup log.
- Host changes to `Mode` and `Events` take effect after an app restart. Tenant overrides take effect on the next tenant configuration refresh.

### Delivery settings (`MassTransit:Outbox:Delivery`)

These tune the delivery loops. The defaults suit normal traffic, so only change them if you have a reason to.

| Setting | Type | Default | Description |
|---|---|---|---|
| `QueryDelay` | TimeSpan (`hh:mm:ss`) | `00:00:05` | How long a loop waits between database polls when it has not been woken by a new message. Lower values reduce worst-case latency but add more polling queries. |
| `QueryMessageLimit` | int | `100` | Maximum number of outboxes (each one is the set of messages from one request) a loop processes per sweep. Set to `1` only if you need strict ordering across different requests. |
| `MessageDeliveryLimit` | int | `100` | Maximum messages delivered from one outbox per pass. |
| `MessageDeliveryTimeout` | TimeSpan | `00:00:10` | Timeout for a single send to Service Bus. On timeout the message stays in the outbox and is retried. |
| `QueryTimeout` | TimeSpan | `00:00:30` | Database timeout for loading outbox messages. |

### Related settings

| Setting | Effect on the outbox |
|---|---|
| `SkipMassTransit` | When `true` (integration tests, code generation) MassTransit is not registered at all, so neither is the outbox. |
| `ConnectionStrings` in each tenant's TenantConfig (`DefaultConnection`) | Decides which EA database stores that tenant's outbox rows and which delivery loop serves it. |

## Configuration examples

### Default: installed but unused (safe for existing subscribers)

```json
"MassTransit": {
  "Outbox": {
    "Enabled": true,
    "Mode": "Allowlist",
    "Events": []
  }
}
```

### Outbox for selected events

```json
"MassTransit": {
  "Outbox": {
    "Enabled": true,
    "Mode": "Allowlist",
    "Events": [ "ApplicationResponseSaved", "ScanRequestedEvent" ]
  }
}
```

### Outbox for every event

Only use this once every subscriber tolerates duplicates.

```json
"MassTransit": {
  "Outbox": {
    "Enabled": true,
    "Mode": "All"
  }
}
```

### Completely off

```json
"MassTransit": {
  "Outbox": {
    "Enabled": false
  }
}
```

### Environment variables / Azure App Service settings

Use a double underscore for nesting and an index for array items:

```text
MassTransit__Outbox__Enabled=true
MassTransit__Outbox__Mode=Allowlist
MassTransit__Outbox__Events__0=ApplicationResponseSaved
MassTransit__Outbox__Events__1=ScanRequestedEvent
MassTransit__Outbox__Delivery__QueryDelay=00:00:05
```

> Array items from environment variables **merge** with items in `appsettings.json` by index. To be sure what the effective list is, keep `Events` in one place and check the startup log.

## Event identifiers you can list

An event is matched if **any** of its identifiers is in `Events` (case-insensitive).

| Kind of event | Published by | Identifiers matched | Example (illustrative) |
|---|---|---|---|
| Virus scan request | `FileUploadedDomainEventHandler` | Class name, full class name | `ScanRequestedEvent` |
| Typed event triggers (`EventTriggers` with `EventKind` `Typed`) | `EventTriggerDispatcher` via `TenantAwareEventPublisher` | Class name (the same name tenants use as `EventType` in `EventTriggers`), full class name | `TransferApplicationSubmittedEvent` |
| Schema event triggers (`EventKind` `Schema`) | `EventTriggerDispatcher` | The `EventType` from the tenant's `EventTriggers` entry, **or** the `TopicName` from `SchemaEvents` | `LsrpApplicationSubmitted` or `lsrp-application-submitted` |
| Prism projection requests | `ProjectionEventPublisher` | Always routed through the outbox; listing it has no effect | `ApplicationProjectionRequestedEvent` |
| Prism template versions | `ProjectionEventPublisher` | Always routed through the outbox; listing it has no effect | `TemplateVersionPublishedEvent` |

The host list applies to **all** tenants. If a typed event is shared by several tenants, adding it to the host list moves it to the outbox for all of them. To change routing for one tenant only, use a per-tenant override.

## Per-tenant overrides

Add a `MassTransit` category to the tenant's settings (Target `Shared`):

```json
{
  "Outbox": {
    "Mode": "Allowlist",
    "Events": [ "TransferApplicationSubmittedEvent", "transfer-application-submitted-schema" ]
  }
}
```

Rules:

- Each key the tenant sets **replaces** the host value for that tenant's events, and keys it leaves out inherit the host value. A tenant `Events` list replaces the host list; it is not merged with it.
- `Enabled: false` opts the tenant out: its events publish directly. A tenant cannot turn the outbox on when the host has `Enabled: false`, because the outbox is then not registered at all.
- Overrides are read when each event is published, so they apply on the next tenant configuration refresh without a restart.
- The startup log line `Transactional outbox routing: ...` shows the host settings only.
- Prism events always use the outbox, whatever the tenant sets.
- Delivery loops already run for every tenant EA database whenever the host has the outbox enabled, so no extra setup is needed. The tenant's database still needs the outbox tables (see below).

## Choosing which events to put through the outbox

Put an event on the allowlist only when **every** subscriber of its topic can safely handle a duplicate message. A subscriber can do this if it:

- ignores a `MessageId` it has already processed, or
- makes naturally idempotent changes (for example "set status to Scanned", not "increment a counter" or "send an email").

If a subscriber cannot handle duplicates, you have two options:

1. **Keep the event off the list** (it stays at-most-once, as today).
2. **Enable Service Bus duplicate detection on its topic.** The outbox resends with the **same `MessageId`**, and Service Bus discards messages whose ID it has already seen within the detection window (for example 10 minutes), so duplicates never reach subscribers. Duplicate detection:
   - can only be set **when a topic is created**, so existing topics must be recreated or replaced;
   - needs the Standard or Premium tier;
   - is an infrastructure change, because `AutoCreateEntities` is `false` in our configuration.

Prism events already use the outbox (Prism re-reads the source and compares revisions, so duplicates are harmless). Move the existing events over one at a time, as their subscribers are confirmed.

## Database setup and migrations

The outbox needs three tables in the `ea` schema of **every tenant EA database**:

| Table | Purpose |
|---|---|
| `ea.OutboxState` | One row per outbox (a set of messages from one request) with lock, delivery and sequence tracking |
| `ea.OutboxMessage` | The serialised messages waiting to be sent, including headers and destination |
| `ea.InboxState` | Used only by consumer-side outboxes (not enabled today); created for completeness |

They are created by the EF migration `AddMassTransitTransactionalOutbox`.

> **Important:** `script/migrate-databases.sh` only migrates the EA database in `ConnectionStrings__DefaultConnection`. If any tenant uses an **isolated** EA database, apply the migration to that database separately. If a tenant's database is missing the tables:
> - business saves still work;
> - allowlisted events for that tenant are **lost** (the follow-up save fails and is logged);
> - that tenant's delivery loop logs errors every few seconds.

The migration only adds tables, so it is safe to apply before deploying the code.

The application's SQL user needs `SELECT`, `INSERT`, `UPDATE` and `DELETE` on the new tables. If permissions are granted at schema level on `ea`, nothing extra is needed.

## Rollout runbook

1. **Apply the migration** to every tenant EA database.
2. **Deploy** with the default (`Mode: Allowlist`, `Events: []`). Behaviour is unchanged.
3. **Check the startup log** for `Transactional outbox routing` and `Started outbox delivery for tenant ...` for each tenant database.
4. **Enable one event in dev**: add it to `Events` and restart.
5. **Test** the action that raises it (for example upload a file for `ScanRequestedEvent`). Check that `ea.OutboxMessage` briefly gets a row and then empties, and that the subscriber receives the message with the expected headers.
6. **Promote** the same setting through staging and production.
7. Repeat for further events once their subscribers are confirmed.

## Turning it off safely

- **Remove a single event**: take it off `Events` and restart. New messages go direct; any of its messages already in the outbox are still delivered.
- **Disable completely** (`Enabled: false`): delivery loops stop, so messages still in the outbox **are not sent** while it is off. Before disabling, check that `ea.OutboxMessage` is empty in every tenant database (see the queries below), or re-enable later to let them drain.

## Monitoring

### Pending messages (run per tenant EA database)

```sql
-- Messages waiting to be delivered
SELECT COUNT(*) AS PendingMessages FROM ea.OutboxMessage;

-- Oldest pending message: should be seconds old, not minutes
SELECT TOP (20) SequenceNumber, MessageId, MessageType, DestinationAddress, SentTime
FROM ea.OutboxMessage
ORDER BY SequenceNumber;

-- Outboxes not yet delivered
SELECT COUNT(*) AS UndeliveredOutboxes FROM ea.OutboxState WHERE Delivered IS NULL;
```

A healthy system has zero or very few rows, all only seconds old. A growing count or old `SentTime` values mean delivery is failing.

### Useful log messages

| Message | Meaning |
|---|---|
| `Transactional outbox routing: Mode ..., Events [...]` | Effective routing at startup |
| `Started outbox delivery for tenant {TenantName}` / `Stopped ...` | Delivery loop lifecycle per tenant database |
| `Failed to start outbox delivery for tenant {TenantName}` | Loop could not start (check the database connection and tables) |
| `Failed to persist integration events to the transactional outbox after commit.` | The follow-up save failed: those events were not stored |
| `Outbox Send Fault` (MassTransit, warning) | A send to Service Bus failed; it will be retried |
| `ProcessMessageBatch faulted` (MassTransit, error) | A delivery sweep failed (for example missing tables or database unavailable) |

## Troubleshooting

| Symptom | Likely cause | What to do |
|---|---|---|
| Event not arriving, nothing in `ea.OutboxMessage` | Event is not allowlisted, so it went direct and failed or was never raised | Check the startup routing log and the identifier spelling; check handler logs |
| Rows pile up in `ea.OutboxMessage` | Service Bus unreachable, the bus is unhealthy, or the topic is missing | Check Service Bus connectivity and topic existence; delivery resumes automatically |
| `Invalid object name 'ea.OutboxMessage'` | Migration not applied to that tenant's database | Apply `AddMassTransitTransactionalOutbox` to the database |
| Only some tenants' events delivered | Tenant has an isolated database without a running loop or without tables | Check for `Started outbox delivery for tenant ...`; apply the migration |
| Subscriber processed a message twice | At-least-once delivery | Make the subscriber idempotent, remove the event from the allowlist, or enable Service Bus duplicate detection |
| No outbox activity at all | `Enabled` is `false`, `SkipMassTransit` is `true`, or `Events` is empty | Check configuration and the startup log |

## FAQ

**Is this per tenant?**
Storage and delivery are per tenant database, and tenant headers are preserved. The routing rule (`Mode` and `Events`) is host-wide and applies to all tenants.

**Does it change topics, payloads or headers?**
No. Messages are published to the same topics with the same body and headers. Only the delivery path changes.

**Does it slow requests down?**
Negligibly. Queuing a message is one extra insert in the tenant database. Sending happens in the background.

**What happens during a Service Bus outage?**
Allowlisted events wait in `ea.OutboxMessage` and are delivered automatically when the bus recovers. Events that are not allowlisted behave as before (logged and lost).

**Are messages delivered in order?**
Messages from the same request are delivered in order. Messages from different requests may be delivered in parallel, which is no different from before.

## Code map

| Component | Location |
|---|---|
| Routing options (`Enabled`, `Mode`, `Events`) | `src/GovUK.Dfe.FlexForms.Utils/Configuration/OutboxOptions.cs` |
| Per-event endpoint selection | `src/GovUK.Dfe.FlexForms.Application/Services/MessageEndpointSelector.cs` |
| Publishers using it | `TenantAwareEventPublisher.cs`, `EventTriggerDispatcher.cs` |
| Outbox registration | `src/GovUK.Dfe.FlexForms.Infrastructure/Messaging/Outbox/TransactionalOutboxRegistrationExtensions.cs` |
| Tenant-aware delivery | `TenantOutboxDeliveryService.cs`, `TenantScopedServiceProvider.cs`, `TenantOutboxNotificationHub.cs` (same folder) |
| Post-commit flush | `src/GovUK.Dfe.FlexForms.Infrastructure/Database/Interceptors/DomainEventDispatcherInterceptor.cs` |
| Outbox tables (model) | `ExternalApplicationsContext.OnModelCreating` |
| Migration | `src/GovUK.Dfe.FlexForms.Infrastructure/Migrations/*_AddMassTransitTransactionalOutbox.cs` |
| Wiring | `Program.cs` → `AddApplicationDependencyGroup(..., configureBusRegistration: ...)` |
| Acceptance tests (Prism events, real SQL Server via Testcontainers, so Docker is required) | `src/Tests/GovUK.Dfe.FlexForms.Api.Tests/Messaging/Outbox/PrismOutboxAcceptanceTests.cs` |
