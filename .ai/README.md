# FlexForms API knowledge index

Tool-agnostic knowledge for coding agents. Start at [`../AGENTS.md`](../AGENTS.md).

## What this repo owns

The API is the system of record for FlexForms. Web renders forms; Prism reports; scanners scan. This process writes users, templates, applications, files, permissions, tenant config, emails, and projection events.

**Verified** from [`README.md`](../README.md) and `src/GovUK.Dfe.FlexForms.Api/Program.cs` (no `Startup.cs`). Target framework: **.NET 10**.

## Read by task

| Task | Read |
|------|------|
| New endpoint / policy | `architecture.md`, `security.md`, `ExpectedSecurity.json` |
| Domain / status / revisions | `domain-model.md`, `features/applications-lifecycle.md` |
| Tenant config / HostMappings | `features/multi-tenancy.md`, `security.md`, `known-pitfalls.md` |
| Token exchange / IdP | `security.md`, `features/token-exchange.md` |
| Files / ClamAV | `integrations.md`, `features/file-scan-pipeline.md` |
| Prism / outbox | `features/prism-projection.md`, `features/transactional-outbox.md` |
| Tenant admin / secrets | `features/tenant-admin.md`, `security.md` |
| Email / Notify | `integrations.md`, `business-rules.md` |
| Permissions / roles | `features/permissions-and-roles.md` |
| Local run / migrations | `development-workflows.md` |
| “Why was this designed this way?” | `decisions/`, `history-lessons.md` |

## Existing human documentation (do not replace)

These remain the detailed operator/developer manuals. `.ai/` files summarise and point here:

- [`../README.md`](../README.md) — architecture, auth, Prism, files, local dev
- [`../docs/multi-tenancy-config.md`](../docs/multi-tenancy-config.md)
- [`../docs/transactional-outbox.md`](../docs/transactional-outbox.md)
- [`../docs/release-process.md`](../docs/release-process.md)
- [`../docs/adrs/`](../docs/adrs/)
- Prism contract lives in **flexforms-prism**: `docs/prism-contract-v1.md`

## Cross-repo knowledge

Do not duplicate Prism SQL views or Web form-engine internals here. Capture only the API side of those contracts, plus pointers.

A future shared `flexforms-knowledge` repository should hold: platform map, Service Bus topic catalogue, Entra app-role matrix, and incident post-mortems. Until it exists, those live in each repo’s `.ai/` plus [`history-lessons.md`](history-lessons.md).
