# Feature: Applications lifecycle

## 1. Purpose

Create, save (append JSON versions), submit once, delete, change owner, invite contributors.

## 2. Entry points

- `Api/Controllers/ApplicationsController.cs`
- Domain `Entities/Application.cs`
- `Infrastructure/.../ApplicationRepository.cs` (`AppendResponseVersionAsync`)
- Commands: `CreateApplicationCommandHandler`, `AddApplicationResponseCommandHandler`, `SubmitApplicationCommandHandler`, `DeleteApplicationCommandHandler`
- Prism: `IProjectionEventPublisher` before commit

## 3. Flow

Create → Saved projection event, revision 1. Each save appends `ApplicationResponse`, increments `SourceRevision`. Submit sets `Submitted`, `SubmittedRevision`, raises `ApplicationSubmittedEvent` (email + integration triggers) and Submitted projection. Delete tombstones for Prism.

Web sends a **full** JSON body (often Base64 on the wire; API stores JSON). The API does **not** merge fields. Partial bodies from a stale Web session have caused data loss — Web now guards with `StaleFormSessionException`.

## 4. Entities

`Application`, `ApplicationResponse`, `File`, contributors via permissions + `AddContributorCommandHandler`.

## 5. Other repos

Web form engine orchestrates pages then calls these APIs. Prism consumes revisioned events.

## 6. Rules

Single submit. Delete blocked if already deleted. Owner change preserves `LastModifiedOn` after submit. File-validation gate on submit.

## 7. Edge cases

- Concurrent saves: repository atomic revision update.
- Post-submit saves still increment revision (Saved events); submitted snapshot uses `SubmittedRevision` / response at submit.
- Custom statuses are labels only.

## 8. Tests

Domain `Application` tests, command handler tests, `PrismOutboxAcceptanceTests`.

## 9. Limits

A true patch-save with `BaseRevision`/409 was recommended after the data-loss incident and is **not shipped**. Do not assume the API merges.
