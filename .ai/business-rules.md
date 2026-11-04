# Business rules and invariants

**Verified** unless marked otherwise.

## Multi-tenancy

- Resolve tenant from `X-Tenant-ID`, else `Origin` → `TenantFrontendOrigins`. Hostname resolve is a dedicated endpoint for Web bootstrap (`GET /v1/tenant-config/resolve`).
- JWT claim `tenant_id` must match the resolved tenant (`EnforceTenantConsistencyAsync`). Cross-tenant tokens are rejected. **Historical:** an LSRP admin token cannot mutate Transfers settings even with a foreign `X-Tenant-ID`.
- Templates belong to a tenant via `Template.TenantId` and/or HostMappings. Catalogue: `TenantTemplateCatalogue`. HostMappings must not list another tenant’s template GUID on a shared EA database.

## Roles

| Role | Scope | Rule |
|------|-------|------|
| SuperAdmin | Platform | `Users.RoleId` = `RoleConstants.AdminRoleId` (`B32B38CA-B90B-4DBF-A788-B4280F0641EF`). Not tenant-assignable. |
| Admin | Tenant | Tenant-scoped `Roles` row + `TenantMembership`. **Never** assign the global Admin GUID as tenant Admin. |
| User | Tenant | Default self-registration membership. |
| Custom | Tenant | Named roles + `RolePermissions`. |
| Caseworker | Legacy | Not assignable; name may persist in tests/docs. |

Self-registration template access (`SelfRegistrationAccessRules` / `SelfRegistrationTemplateAccessService`):

- 0 live templates → none
- 1 live template → that template
- Many live templates → none unless `SelfRegistration:DefaultTemplateId` (legacy key `ExternalApplicationsApiClient:DefaultTemplateId`) is a live form

## Permissions

Claim format: `{ResourceType}:{ResourceKey}:{AccessType}` e.g. `Template:Any:Manage`, `Template:{guid}:Read`.

Merged from `RolePermissions` + user `Permissions` (`UserPermissionClaimProvider`). Evaluated by `PermissionClaimEvaluator` / policy handlers.

`CanWriteApplicationFiles` is **not** sufficient for the file-validation callback. That requires `FileValidation:Any:Write` (or template-scoped) and typically `IsServicePrincipal: true` on the API-key provider. Interactive Admin/SuperAdmin callers get **403** on the callback.

## Templates

- New templates start **not live** (`Template.SetLive`).
- Saving a version does **not** change in-flight applications. Applicants stay on the version they started; new apps use the latest live version.
- Once a template is in use, new versions must declare removed/renamed fields (`retiredFields` / `semanticKey`). This is the Prism reporting contract. Recycling `field_id`s breaks historical joins.
- Custom application statuses are **display labels** mapped onto the existing `ApplicationStatus` enum. They do not add transitions.

## Submit

- Single submit (`Application.Submit` throws if already submitted).
- File-validation gate (`ApplicationFileValidationPolicy`): `Off` | `FailOnInvalid` | `RequirePassed`.
- `RequirePassed` can strand applicants if the tenant validation function is down. There is **no** timeout escape hatch yet (**Documented** in README as a later increment).
- Infected files are deleted before they can remain on a submitted application.

## Files vs tenant validation

| Path | Owner | Failure behaviour |
|------|-------|-------------------|
| ClamAV virus scan | Platform (Service Bus) | File deleted |
| Tenant Excel/schema callback | Tenant function → `POST /v1/integrations/files/{id}/validation-result` | File kept, status `Failed`; may block submit |

Do **not** let tenant functions publish onto FlexForms Service Bus.

## Email

- GOV.UK Notify template IDs live in tenant `EmailTemplates`, not in form JSON.
- Invalid Notify UUIDs fail at Notify (HTTP 400), not at settings save, unless validators have been added since. Prefer validating GUID format on upsert.
- Optional `EmailPlaceholderMappings` map form answers into Notify personalisation (`EmailPersonalisationBuilder`).
- Submit emails are best-effort relative to the OLTP commit (domain event after save). Prism projection is the guaranteed path.

## Secrets

- Secret categories encrypted with Data Protection purpose `TenantSettings.v1`.
- List/validate replace secret leaves with `__REDACTED__`. Upsert restores sentinel leaves from stored values.
- SuperAdmin plaintext: Dev/Test/Local only (`TenantSettingSecretPlaintextGate`). Production SuperAdmin uses audited `POST .../settings/reveal`.
- UI masking in Web is **not** access control.

## SuperAdmin-only setting categories

Cannot be edited by Tenant Admins: `ConnectionStrings`, `ApplicationTemplates`, `Template`, `FileStorage`, `Email` (see README + `SuperAdminOnlyTenantSettingCategories`).
