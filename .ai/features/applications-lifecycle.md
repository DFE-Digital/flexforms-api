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

Web sends a **full** JSON body. Add-response (`AddApplicationResponseCommandHandler`) **requires Base64** and stores the decoded JSON. Create (`CreateApplicationCommandHandler`) stores `InitialResponseBody` **verbatim** — Web sends raw `"{}"`; any caller sending Base64 there (Playwright did, `e30=`) leaves an encoded body until the first save, so readers must tolerate both (**Verified**). The API does **not** merge fields. Partial bodies from a stale Web session have caused data loss — Web now guards with `StaleFormSessionException`.

## 4. Entities

`Application`, `ApplicationResponse`, `File`, contributors via permissions + `AddContributorCommandHandler`.

Contributor invites (**Verified**, `ContributorInviteAccess`, `AddContributorCommandHandler`): template `contributorRoleId` still decides **tenant membership** only. When `AddContributorRequest.Permissions` is set, direct Application / ApplicationFiles grants come from those flags (Application Read always; files Read is implied by Upload or Delete). When `Permissions` is null, legacy behaviour remains (User role → full edit; other roles → read-only). Re-invite **replaces** Application / ApplicationFiles grants on that application — inviting the application owner's email is rejected so those grants are not replaced. Merge emits `ContributorInvite:{appId}:Scope` from user-level Application grants; that marker (not a role `Application:{id}` claim) blocks role `ApplicationFiles:Any` / concrete file grants the invite omitted.

Same-name upload collision (**Verified**, `UploadFileCommandHandler`): an orphaned file is replaced when the caller has ApplicationFiles Delete, or when they uploaded the orphan themselves; otherwise the upload conflicts like a live duplicate.

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

Over HTTP with real tokens (**Verified**, `TwoTenant/ApplicationAccessWorkflowTests`): an admin can save a response on another user's application; a member gets 403 opening an application not shared with them and doesn't see it on their dashboard; only the lead applicant can submit (an admin gets 403 and the status is unchanged); a contributor added by the owner sees and can edit the application, and gets 403 again once removed; an admin sees an application's contributors and, by the lead applicant's email, the applications they created with their invitees; a role can't be deleted while someone holds it (400) and can once they're moved off it; a user removed from the tenant can no longer exchange a token (403).

## 9. Limits

A true patch-save with `BaseRevision`/409 was recommended after the data-loss incident and is **not shipped**. Do not assume the API merges.
