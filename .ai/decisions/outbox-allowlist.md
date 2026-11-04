# Transactional outbox allowlist

- **Status:** Accepted
- **Provenance:** **Verified** — `docs/transactional-outbox.md`, `MessageEndpointSelector`, `OutboxOptions`.

## Context

SQL commit and Service Bus send cannot share a transaction. Lost scan/analytics messages vs duplicate delivery to consumers that are not idempotent.

## Decision

MassTransit EF outbox exists, **enabled by default**, but **Mode = Allowlist** with **empty Events**. Prism events are hard-coded into `AlwaysOutboxEvents`. Other events publish directly unless listed.

Delivery is per tenant EA database (`TenantOutboxDeliveryService`). MassTransit’s built-in delivery service is disabled because it would only drain the first tenant DB.

## Alternatives

Outbox for every event was rejected because some subscribers cannot handle duplicates. Topic duplicate detection can only be enabled at topic creation.

## Consequences

- Scan publish can still be lost on a bus outage unless `ScanRequestedEvent` is allowlisted **and** consumers are duplicate-safe.
- Isolated tenant DBs without outbox tables break Prism for that tenant.
