# Feature: Templates and versions

## 1. Purpose

Versioned JSON form schemas. Live flag. Catalogue for a tenant.

## 2. Entry points

- `Entities/Template.cs`, `TemplateVersion.cs`
- `TemplatesController`, `CreateTemplateCommandHandler`, `CreateTemplateVersionCommandHandler`
- `TenantTemplateCatalogue`, `TemplateHostMappingOwnershipValidator`
- Compatibility: `TemplateFieldCompatibilityPolicy` (retired fields / semantic keys)

## 3. Flow

Admin creates draft template → adds immutable versions (JSON) → sets `IsLive` → grants access. Each new version publishes `TemplateVersionPublishedEvent` (outbox) so Prism can catalogue fields before anyone answers them.

## 4. Entities

`Template`, `TemplateVersion`, HostMappings in TenantSettings `ApplicationTemplates`.

## 5. Other repos

Web Template Manager / Template Studio author JSON. Prism catalogues fields and export policy. Changing field ids requires data-team awareness (Web save acknowledgement).

## 6. Rules

Versions immutable. In-flight apps stay on old version. `retiredFields` required when in use. Do not recycle field ids.

## 7. Edge cases

Legacy templates with `TenantId == null` exist only via HostMappings. JSON size: Web form limits historically returned 400 on huge Template Manager posts (**Unverified** here — see Web).

## 8. Tests

Template command handlers, compatibility policy tests, Prism template-version outbox tests.

## 9. Limits

Schemaless JSON vs reporting is the original ADR tension; Prism long-facts is the mitigation.
