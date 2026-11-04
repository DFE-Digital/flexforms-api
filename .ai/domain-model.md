# Domain model

**Verified** in `src/GovUK.Dfe.FlexForms.Domain`. Shared enums (`ApplicationStatus`, `FileValidationStatus`, `FileValidationMode`, `AccessType`, `ResourceType`) live in **`GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums`**, not this repo.

## Aggregates (`ea`)

| Type | Path | Notes |
|------|------|--------|
| `Application` | `Entities/Application.cs` | Reference, template version, status, `SourceRevision`, `SubmittedRevision` |
| `ApplicationResponse` | `Entities/ApplicationResponse.cs` | JSON body; `CreatedAtRevision` |
| `Template` | `Entities/Template.cs` | `TenantId?`, `IsLive`, versions |
| `TemplateVersion` | `Entities/TemplateVersion.cs` | Immutable schema JSON |
| `File` | `Entities/File.cs` | Storage path, `FileValidationStatus` |
| `User` | `Entities/User.cs` | Email, external id, platform `RoleId` |
| `Role` | `Entities/Role.cs` | Global vs tenant-scoped (`TenantId` nullable) |
| `TenantMembership` | `Entities/TenantMembership.cs` | User ↔ tenant ↔ tenant role |
| `RolePermission` | `Entities/RolePermission.cs` | Role → claim grants |
| `Permission` | `Entities/Permission.cs` | Per-user overrides; optional `ApplicationId` |
| `CustomApplicationStatus` | `Entities/CustomApplicationStatus.cs` | **Labels only** — does not add state-machine transitions |
| `TenantAccessAudit` | `Entities/TenantAccessAudit.cs` | Tenant-admin access audit |

There is **no** `TemplatePermission.cs` entity today. README ER diagram still mentions `TemplatePermission`; that is **stale**. Access is claim-based (`RolePermissions` + `Permissions`). Migration history still contains `TemplatePermissions` tables — do not revive without a new ADR.

## Tenancy entities (`tenantconfig`)

`TenantEntity`, `TenantSettingEntity`, `TenantHostnameEntity`, `TenantFrontendOriginEntity`, `TenantPrincipalEntity`, `TenantSettingAuditEntity` under `Domain/Tenancy/Entities/`.

Settings uniqueness: **`(TenantId, Category, Target)`** with Target `Shared` | `Api` | `Web`.

## Value objects

Strongly typed ids in `Domain/ValueObjects/StronglyTypedIds.cs`: `RoleId`, `UserId`, `TemplateId`, `TemplateVersionId`, `ApplicationId`, `ResponseId`, `PermissionId`, `FileId`, `CustomApplicationStatusId`, `TenantMembershipId`, `RolePermissionId`.

## Application lifecycle

Not a state-machine library. Guards live on `Application`:

- Default status `Created` if null.
- `AddResponse` increments `SourceRevision` and stamps `CreatedAtRevision`.
- `Submit` → `Submitted`; throws if already submitted; sets `SubmittedRevision = SourceRevision`.
- `Delete` → `Deleted`; throws if already deleted.
- `ChangeOwner` blocked when deleted; **does not** update `LastModifiedOn` when already submitted (listing projection uses that as submission timestamp).

Applications **pin** `TemplateVersionId` at creation. New versions do not rewrite in-flight applications.

## File validation statuses

`File.cs`: `NotRequired` → `Pending` → `Passed` / `Failed`. Infected ClamAV files are **deleted**; failed tenant validation **marks** the file and may block submit (`ApplicationFileValidationPolicy`).

## Domain events

`Domain/Events/`: `ApplicationCreatedEvent`, `ApplicationResponseAddedEvent`, `ApplicationSubmittedEvent`, `ApplicationDeletedEvent`, `FileUploadedDomainEvent`, `FileDeletedEvent`, `FileValidationRecordedEvent`, `ContributorAddedEvent`, `ContributorPermissionsGrantedEvent`, `UserCreatedEvent`.

Side effects (email, scan publish, cache invalidation) belong in Application event handlers, not aggregates.

## Prism revisions

`SourceRevision` is also the optimistic concurrency token. Concurrent saves go through `ApplicationRepository.AppendResponseVersionAsync` (atomic SQL `UPDATE` inside a transaction). Do not increment the revision in a way that can collide.

Response JSON is the OLTP system of record (append-only versions). Prism is a derived read model. **Do not** replace JSON answers with normalized OLTP field tables without revisiting ADR `docs/adrs/20251125_configurable_json_templates.md`.
