# FlexForms API — Agent entry point

Backend for **FlexForms**: a multi-tenant, template-driven GOV.UK form platform. Tenants (Transfers, Visits, LSRP, …) share one API. Persistence, auth, permissions, files, email, events, and Prism projection live here. The companion UI is `flexforms-web`.

This file is the primary entry for any AI coding agent. Do not load the whole knowledge base. Read this, then only the `.ai/` files that match the task.

## Ecosystem

| Repo | Role |
|------|------|
| **flexforms-api** (this repo) | System of record. REST + SignalR + Service Bus publishers/consumers. |
| **flexforms-web** | Razor Pages form engine and admin. Calls this API via `GovUK.Dfe.FlexForms.Api.Client`. |
| **flexforms-prism** | Analytics projection. Consumes outbox events; reads `/v1/internal/prism`. |
| **DfE.CoreLibs** | Shared NuGet packages (contracts, security, messaging, caching, email, files). |
| **rsd-file-scanner-function** | Consumes `file-scanner-requests`; calls ClamAV; publishes `file-scanner-results`. |
| **rsd-clamav-api** | ClamAV HTTP engine. Not called directly by this API. |

Product branding is FlexForms. Many types still use the historical name **External Applications** (`ExternalApplicationsContext`, `GovUK.Dfe.CoreLibs.Contracts.ExternalApplications`). Do not rename those without a coordinated CoreLibs + client bump.

## Constraints (do not violate)

1. **Clean Architecture.** Domain has no infrastructure. Application is MediatR CQRS. Controllers stay thin.
2. **Tenant on every request.** `TenantResolutionMiddleware` + JWT `tenant_id` must match. Weakening this leaks data.
3. **CoreLibs via NuGet**, not local `ProjectReference`, unless you control the entire graph. Local CoreLibs builds as `1.0.0.0` and breaks consumers compiled against published assembly versions.
4. **Prism events always use the transactional outbox** (`MessageEndpointSelector.AlwaysOutboxEvents`). Publish **before** commit. The event is a nudge, not a payload.
5. **Never change `RoleConstants.AdminRoleId`**. That GUID is the platform SuperAdmin shell. Tenant Admin is a different, tenant-scoped `Roles` row.
6. **Do not recycle template `field_id`s.** Applications pin `TemplateVersionId`. Field identity is the reporting contract.
7. **Do not swap Azure File Share for Blob** without revisiting ADR `docs/adrs/20251125_use_azure_file_share.md`.
8. **Do not scan files synchronously** in the upload request. Scan is async via Service Bus.
9. **EA migrations must reach every tenant EA database**, including isolated ones. Host `DefaultConnection` is not enough.
10. After adding or changing an authorised endpoint, update `src/Tests/GovUK.Dfe.FlexForms.Api.Tests/Security/ExpectedSecurity.json`.

## How to understand a change before coding

1. Identify the feature (applications, templates, tenancy, files, Prism, auth, tenant admin).
2. Read the matching file under [`.ai/features/`](.ai/features/) and [`.ai/known-pitfalls.md`](.ai/known-pitfalls.md).
3. Open the cited source files. Treat this knowledge as a map, not a substitute for the code.
4. If the task spans Web or Prism, read those repos’ `AGENTS.md` as well. Cross-repo flows: [`.ai/integrations.md`](.ai/integrations.md).

## Knowledge map

| File | Use when |
|------|----------|
| [`.ai/README.md`](.ai/README.md) | Index of this knowledge base |
| [`.ai/architecture.md`](.ai/architecture.md) | Layers, entry points, dual databases, request pipeline |
| [`.ai/domain-model.md`](.ai/domain-model.md) | Aggregates, value objects, revisions, status |
| [`.ai/business-rules.md`](.ai/business-rules.md) | Invariants, submit gates, self-registration, live templates |
| [`.ai/security.md`](.ai/security.md) | Auth schemes, token exchange, roles, secrets |
| [`.ai/integrations.md`](.ai/integrations.md) | Service Bus, SignalR, ClamAV, Notify, Prism |
| [`.ai/dependencies.md`](.ai/dependencies.md) | NuGet, other repos, version coupling |
| [`.ai/development-workflows.md`](.ai/development-workflows.md) | Local run, migrations, NSwag, tests, release |
| [`.ai/known-pitfalls.md`](.ai/known-pitfalls.md) | Incidents and footguns (code-verified) |
| [`.ai/history-lessons.md`](.ai/history-lessons.md) | Lessons from past incidents and design work |
| [`.ai/decisions/`](.ai/decisions/) | ADRs (existing + captured) |
| [`.ai/features/`](.ai/features/) | Deep dives |

Human docs that remain authoritative: [`README.md`](README.md), [`docs/multi-tenancy-config.md`](docs/multi-tenancy-config.md), [`docs/transactional-outbox.md`](docs/transactional-outbox.md), [`docs/test-auth-authenticator-setup-codes.md`](docs/test-auth-authenticator-setup-codes.md), [`docs/adrs/`](docs/adrs/).

## Knowledge maintenance

Agents **must**:

- Read relevant `.ai/` files before substantial changes.
- Validate documented claims against current source. If code and docs disagree, trust the code and update the docs.
- Update affected `.ai/` files when behaviour or architecture changes.
- Add an ADR under `.ai/decisions/` (and `docs/adrs/` when the team wants a human-facing record) for significant decisions.
- Not introduce undocumented deviations from existing patterns.
- Never silently overwrite **Verified** knowledge with speculation. Label inferences as **Unverified**.
- Keep knowledge changes in the same pull request as the code change so they are reviewable.

### Definition of done (mandatory)

This is enforced in CI: the **Knowledge base check** job fails a pull request that changes production code under `src/` (tests excluded) without touching `AGENTS.md` or `.ai/`, unless the PR has the `no-docs` label. It runs first; build and tests only start once it passes. After adding the label, re-run the failed job.

A task is **not complete** until you have checked whether it changed anything the knowledge base describes. Before finishing:

1. Check the change against the triggers below.
2. Update every affected `.ai/` file in the same change, citing the source files you touched.
3. In your final summary or PR description, list the `.ai/` files you updated, **or** state “No knowledge impact” with a one-line reason.

| If you changed… | Update |
|-----------------|--------|
| An endpoint, policy, auth scheme or role | `.ai/security.md`, `ExpectedSecurity.json`, matching `.ai/features/` file |
| An entity, value object, status or invariant | `.ai/domain-model.md`, `.ai/business-rules.md` |
| A TenantSettings category, Target, or tenant resolution | `.ai/features/multi-tenancy.md`, `.ai/business-rules.md` |
| A Service Bus topic, event or consumer | `.ai/integrations.md` — and flag the CoreLibs/Web/Prism/scanner repos that must change too |
| Prism events, revisions or `/v1/internal/prism` DTOs | `.ai/features/prism-projection.md` — and flexforms-prism `docs/prism-contract-v1.md` |
| Migrations, local setup, CI or release steps | `.ai/development-workflows.md` |
| A fix for an incident or footgun | `.ai/known-pitfalls.md` (and `.ai/history-lessons.md` if the lesson is worth keeping) |
| A significant design choice | New file in `.ai/decisions/` |
| Something that makes an existing `.ai/` statement wrong | Correct or delete that statement — never leave it stale |

These repositories are **public**. Never write secrets, credentials, connection strings, tenant secret values, or descriptions of unfixed security weaknesses into `.ai/` or `AGENTS.md`. Record the rule (“never log SAS URLs”), not the vulnerability.

Provenance labels used in `.ai/` files:

- **Verified** — confirmed in current source.
- **Documented** — existing README/ADR/manual, still consistent with code.
- **Historical** — from past engineering work (incidents, reviews); validated against code where noted.
- **Unverified** — from discussion or inference; do not treat as current behaviour until checked.
