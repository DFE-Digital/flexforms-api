# Feature: Transactional outbox

Deep operator guide: [`../../docs/transactional-outbox.md`](../../docs/transactional-outbox.md).

## Agent summary

- Tables: `ea.OutboxState`, `ea.OutboxMessage`, `ea.InboxState` per tenant EA database.
- Selector: `MessageEndpointSelector` + `TenantOutboxRouting`.
- Delivery: `TenantOutboxDeliveryService` (one loop per distinct EA connection).
- Always-outbox: `ApplicationProjectionRequestedEvent`, `TemplateVersionPublishedEvent`.
- Default allowlist: empty. At-least-once; duplicates possible.

Publish Prism events **inside** the business transaction (before commit). Domain-event handlers that run after save need a follow-up `SaveChanges` to land in the outbox — that path is weaker; do not use it for Prism.
