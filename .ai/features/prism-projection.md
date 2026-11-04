# Feature: Prism analytics feed

## 1. Purpose

Tell Prism when applications or template versions change, and serve read-only source data.

## 2. Entry points

- `Application/Services/ProjectionEventPublisher.cs`
- `Application/Services/MessageEndpointSelector.cs` (`AlwaysOutboxEvents`)
- Command handlers: create/save/submit/delete application; create template / template version
- `Api/Controllers/InternalPrismController.cs`
- Queries: `Application/Prism/Queries/`
- `Infrastructure/Services/PrismReportingExportPolicyService.cs`

## 3. Flow

Change + revision + outbox row in **one** EA transaction → `TenantOutboxDeliveryService` → topic `flexforms-prism` session `{tenantId}:{applicationId}` (or `{tenantId}:template:{templateId}`) → Prism reads internal GET endpoints → flatten.

## 4. Contracts

CoreLibs `ApplicationProjectionRequestedEvent`, `TemplateVersionPublishedEvent`. DTOs in `GovUK.Dfe.CoreLibs.Contracts` `PrismDtos.cs`. Authoritative narrative: flexforms-prism `docs/prism-contract-v1.md`.

## 5. Other repos

flexforms-prism Function App. Web reporting-export admin UI calls API which calls Prism control API.

## 6. Rules

Event is a nudge. `Prism.Read` platform role only. Tenant-scoped GETs 404 as if missing. `current` endpoint reads revision first then responses `CreatedAtRevision` ≤ revision.

## 7. Edge cases

- `MassTransit:Outbox:Enabled = false` loses the all-or-nothing guarantee (`NoOpProjectionEventPublisher` when bus skipped).
- Missing outbox tables on isolated EA DBs.
- `ContractVersion` mismatches are dead-lettered by Prism.

## 8. Tests

`PrismOutboxAcceptanceTests`, `ProjectionEventPublisherTests`, `MessageEndpointSelectorTests`, Prism security tests.

## 9. Limits

Do not add a parallel lake worker that flattens from EventMappings. Export policy lives in Prism, not EA.
