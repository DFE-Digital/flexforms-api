# Prism events are notifications, not payloads

- **Status:** Accepted
- **Provenance:** **Verified** in README Prism section, `ProjectionEventPublisher`, flexforms-prism `docs/prism-contract-v1.md`. Design came out of the data-pipeline architecture review that preceded Prism.

## Context

JSON templates change often. A wide analytics table or fat Service Bus payload (256 KB Standard limit) cannot keep up. Reporting must not drift from OLTP.

## Decision

1. Increment `SourceRevision` in the same transaction as the data change.
2. Publish a thin `ApplicationProjectionRequestedEvent` (ids + revision + reason) via the transactional outbox — **always**, regardless of allowlist.
3. Prism re-reads `/v1/internal/prism` and compare-and-swaps generations.
4. Template versions get `TemplateVersionPublishedEvent` the same way.

## Alternatives

Lake flatten from EventMappings was rejected: EventMappings are intentional **subsets** for integrations. Draft-only or submit-only projection was discussed; **current code projects Saved, Submitted, and Deleted**.

## Consequences

- Duplicate/out-of-order messages are harmless if Prism compares revisions.
- Do not put answer JSON on the bus.
- Do not build a second flatten pipeline alongside Prism.
