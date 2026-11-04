# Architecture decisions

## Existing ADRs (human-facing, do not duplicate)

| ADR | Path |
|-----|------|
| JSON templates for multi-site platform | [`../../docs/adrs/20251125_configurable_json_templates.md`](../../docs/adrs/20251125_configurable_json_templates.md) |
| Azure File Share (not Blob) | [`../../docs/adrs/20251125_use_azure_file_share.md`](../../docs/adrs/20251125_use_azure_file_share.md) |
| ClamAV for virus scanning | [`../../docs/adrs/20251125_use_clamav_for_virus_scanning.md`](../../docs/adrs/20251125_use_clamav_for_virus_scanning.md) |
| Service Bus + SignalR for async scan UX | [`../../docs/adrs/20251125_azure_service_bus_and_signal_r.md`](../../docs/adrs/20251125_azure_service_bus_and_signal_r.md) |
| SQL temporal tables for audit | [`../../docs/adrs/20251125_temporal_tables_for_auditing.md`](../../docs/adrs/20251125_temporal_tables_for_auditing.md) |

Those ADRs still say “External Applications” — historical product name. Decisions remain in force.

## Captured from implementation (rationale sometimes incomplete)

| Decision | File |
|----------|------|
| Dual TenantConfig + EA databases | [`dual-database-tenantconfig.md`](dual-database-tenantconfig.md) |
| Token exchange BFF | [`token-exchange-bff.md`](token-exchange-bff.md) |
| Prism events are nudges + always-outbox | [`prism-events-as-nudges.md`](prism-events-as-nudges.md) |
| Outbox allowlist (empty by default) | [`outbox-allowlist.md`](outbox-allowlist.md) |
| CoreLibs NuGet, not project references | [`corelibs-nuget-not-project-refs.md`](corelibs-nuget-not-project-refs.md) |

When recording a new decision: context, decision, alternatives **only if known**, consequences, code references. Do not invent historical alternatives.
