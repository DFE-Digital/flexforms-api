# Feature: File upload, virus scan, tenant validation

## 1. Purpose

Store uploads, scan for malware asynchronously, optionally run tenant schema validation, notify the user.

## 2. Entry points

- `UploadFileCommandHandler`, `FileUploadedDomainEventHandler`
- `Application/Consumers/ScanResultConsumer.cs`
- `Api/Controllers/IntegrationsController.cs` + `RecordFileValidationResultCommandHandler`
- Domain `File.cs`, `ApplicationFileValidationPolicy.cs`

## 3. Flow

Upload → File Share → domain event → `ScanRequestedEvent` (`file-scanner-requests`) → scanner → ClamAV `/scan/async/url` → `ScanResultEvent` → API consumer deletes infected files, updates metadata, Redis notification + SignalR.

If template `FileValidation` mode ≠ Off and extension eligible: status `Pending`; tenant function callbacks `POST /v1/integrations/files/{fileId}/validation-result`; `Passed`/`Failed`; submit gated.

## 4. Entities / events

`File`, `FileUploadedDomainEvent`, `FileValidationRecordedEvent`, CoreLibs `ScanRequestedEvent` / `ScanResultEvent`.

## 5. Other repos

`rsd-file-scanner-function`, `rsd-clamav-api`, **flexforms-web ScanResultConsumer** (session/UX). Tenant functions are external.

## 6. Rules

Virus scan is platform-owned. Tenant validation must not publish to FlexForms bus. Infected → delete; invalid → keep + maybe block submit.

The callback's `message` is stored **as sent** (trimmed, max `File.ValidationMessageMaxLength` = 1000) and may be Markdown (**Verified**). The API never renders it; Web does, through `MarkdownSafe.RenderIntegrationMessage` (see flexforms-web `.ai/integrations.md`). The failed-notification text is `We could not validate '{file}'. {message}` with the raw message also in `Metadata["message"]`; Web relies on that suffix to render the detail, so keep the two in step if you change `FileValidationRecordedEventHandler`.

## 7. Edge cases

- SAS must remain valid through download+scan (typically 1h).
- Scanner Redis cache by `FileHash` can replay a result for a new requester (routing stamps tenant/user on cache hit).
- Local instance-id filter on consumers.
- Hard-coded test filename in scanner fakes infection (**scanner repo**).

## 8. Tests

`ScanResultConsumerTests`, file command tests, `ApplicationFileValidationPolicyTests`.

## 9. Limits

Scan events are not always-outbox. ClamAV jobs are in-memory on the ClamAV API (not durable across replicas). ADR: ClamAV + Service Bus/SignalR.
