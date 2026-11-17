# Feature: Tenant outbound events (EventTriggers, EventMappings, SchemaEvents)

**Verified** in source on 2026-11-17. Before this file existed the feature was only mentioned in passing ("EventMappings are not the reporting contract").

## 1. Purpose

Let a tenant publish integration events to Azure Service Bus when something happens to an application, with a payload built from form answers and platform metadata, without code changes. Example: Transfers publishes `TransferApplicationSubmittedEvent` to `transfer-application-submitted` on submit.

This is **not** the reporting path (Prism is) and **not** the virus-scan path (`ScanRequestedEvent` is published by the platform and cannot be configured).

## 2. Entry points

| Piece | Code |
|-------|------|
| Dispatcher | `Application/Services/EventTriggerDispatcher.cs` (`DispatchAsync(trigger, applicationId, reference, templateId, formData, platformMetadata)`) |
| Callers | `ApplicationSubmittedEventHandler` (trigger `ApplicationSubmitted`), `FileUploadedDomainEventHandler` (trigger `FileUploaded`) |
| Trigger names | `Application/Options/EventTriggerType.cs` (`ApplicationSubmitted`, `FileUploaded`) |
| Options | `EventTriggersOptions` (`EventTriggers` category: trigger → list of `{ eventKind, eventType, mappingId }`), `SchemaEventsOptions` (`SchemaEvents` category: name → `{ topicName, version, description, jsonSchema }`) |
| Mappings | `EventMappingProvider` (`EventMappings:{templateId}:{eventType}`; falls back to any template key holding that event type, because admin UI may save under the API template GUID while the schema uses a legacy `templateId` such as `form-001`), `EventDataMapper`, `FieldMappingValueExtractor` |
| Mapping DSL | `Domain/Models/EventMapping/` — `EventFieldMapping`, `FieldMapping` with `FieldMappingSourceType`: `DirectField`, `ComplexFieldProperty`, `Collection`, `Computed`, `Static`, `Metadata` |
| Metadata keys | `Application/Options/PlatformEventMetadataKeys.cs` (always: `applicationId`, `applicationReference`; submit: `submittedBy*`, `submittedOn`; upload: `fileId`, `fileName`, `filePath`, `fileUri`, `fileHash`, `uploader*`, `leadApplicantName`, …) |
| Typed events | `EventTypeRegistry` (CoreLibs Messaging.Contracts records, e.g. `TransferApplicationSubmittedEvent`) |
| Endpoint choice | `MessageEndpointSelector` (outbox allowlist: `MassTransit:Outbox` host options, overridable per tenant) |
| Admin catalogue | `GET /v1/admin/tenants/event-catalogue` (`GetEventCatalogueQuery`, `Messaging/PlatformEventCatalogueBuilder`) |
| Web admin UI | flexforms-web `Pages/Admin/EventMappings` → `EventMappingsAdminService`, `SchemaEventDefinitionProvider`; sample mapping `Web/EventMappings/form-001/TransferApplicationSubmittedEvent.json` |

## 3. Flow

1. Domain event handler runs after commit and calls `DispatchAsync` with the trigger name.
2. Entries come from `EventTriggers:{trigger}`; the legacy `ApplicationSubmission:PublishEvent` section is still honoured for submit.
3. Per entry: `eventKind` `Typed` (default) resolves a CoreLibs record type and maps it; `Schema` maps to a dictionary and wraps it in a `SchemaEventEnvelope` (`MessageType`, `Version`, `TopicName`, `Payload`, `Metadata`) sent to the `SchemaEvents` topic.
4. Messages carry custom properties `serviceName = extapi-{tenantName}` and `eventKind`.
5. Failures are logged per entry and do not fail the submit/upload (best effort, post-commit).

## 4. Rules

- `ScanRequestedEvent` in `EventTriggers` is skipped with a warning; scanning is always on.
- Mappings are intentional subsets for a consumer. Never repurpose them as a reporting feed.
- Unknown typed event names and missing schema topics are skipped, not errors.
- Put an event type on the outbox allowlist (`MassTransit:Outbox`) when its consumer needs transactional delivery.
- Do not map `fileUri` into an event unless that consumer must read the file, and never log it.

## 5. Other repos

New typed events need a record (and topic constant) in CoreLibs Messaging.Contracts first. Schema events need only TenantConfig plus a topic that exists. Consumers live outside this workspace.

## 6. Tests

`Application.Tests/Services/MessageEndpointSelectorTests.cs` and the dispatcher / mapper tests under `Application.Tests/Services/`.
