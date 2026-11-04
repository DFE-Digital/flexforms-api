# Feature: Tenant admin and settings

## 1. Purpose

CRUD tenant settings, secrets, import/export, duplicate tenant, refresh catalogue.

## 2. Entry points

- `TenantAdminController`
- `UpsertTenantSettingCommandHandler`, `TenantSettingJsonValidator`
- `ITenantSettingsEncryptor`, `TenantSettingSecretPlaintextGate`
- `TenantDuplicatorService`

## 3. Flow

Admin UI (Web) → API upsert with Target Shared/Api/Web. Secrets encrypted. List/validate redact. Production SuperAdmin reveal is audited.

## 4. Entities

`TenantSettingEntity`, audits, principals.

## 5. Other repos

Web Tenant Settings page (masking is UX only). Scripts in `scripts/` for bootstrap.

## 6. Rules

SuperAdmin-only categories. Sentinel `__REDACTED__` restore. Overlap gate: same flattened keys must not exist on Shared and Api/Web.

## 7. Edge cases

Duplicator copying InternalServiceAuth to overlapping targets. Refresh fail-closed. Reveal is break-glass, not a substitute for RBAC.

## 8. Tests

`TenantSettingSecretPlaintextGateTests`, `TenantSettingTargetOverlapGateTests`, TenantAdmin handler tests.

## 9. Limits

Settings changes reach running instances on the next refresh, not immediately.
