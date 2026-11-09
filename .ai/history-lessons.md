# History lessons

Lessons from past engineering work on FlexForms (incidents, design reviews, production debugging). They are **not** a substitute for source. Each item states whether current code still matches. Do not treat **Historical** or **Unverified** items as current behaviour until checked.

## Data and config incidents

| Lesson | Status |
|--------|--------|
| Full response bodies are trusted; the API does not merge. A partial Web payload becomes the new version. | **Verified** — save handlers append the posted body. Web now throws `StaleFormSessionException` on a mismatched session application id. |
| Tenant config refresh is all-or-nothing; the old tenant duplicator creating overlapping Shared + Api keys crashed flattening for every tenant. | **Verified** — `TenantSettingTargetOverlapGate`, `TenantSettingTargetOverlapGateTests`. The duplicator was replaced by minimum-settings New tenant (`decisions/new-tenant-minimum-settings.md`). |
| Secret leaves must be redacted in list **and** validate responses; sentinel restore keeps stored values on save. | **Verified** — README secret table + redaction. |
| Local Data Protection path / `ApplicationName` mismatches looked like DB corruption. | **Verified** — `TenantSettingsDataProtectionExtensions`. |
| Notify “template not found” was usually a non-hex UUID in `EmailTemplates`, not a HostMappings problem. | **Historical** — still the lookup model; validators may have tightened. |

## Auth and tenancy

| Lesson | Status |
|--------|--------|
| Test Auth HMAC keys must be ≥16 bytes; Web Target overrides Shared; Web caches tenant config per replica. | **Verified** (key size / merge). Cache duration: check `PlatformBootstrap:TenantConfigurationCacheMinutes` on Web. |
| OTP lives on the API (Redis + email); Web mints the JWT after verify. | **Verified** — OTP commands/endpoints exist. |
| `Prism.Read` belongs on the **platform** app registration; client-credentials `roles`, not delegated `scp`. | **Verified** — `PlatformPrismRead` policy. |
| “TokenEndpoint is required” on Entra-only tenants is Web TokenRefresh options, not the API. | **Verified** on Web. |
| HostMappings ownership checks must keep legacy `TenantId == null` templates working. | **Verified** — catalogue + validators. |
| Redis keys are prefixed `t:{tenantId}:`; a foreign prefix must throw. | **Verified** — `TenantCacheKeyHelper`. |

## Messaging, SignalR, Prism

| Lesson | Status |
|--------|--------|
| Analytics uses long facts (`answer_facts` + catalogue), not wide tables per template edit. EventMappings are **not** the reporting contract. | **Verified** in Prism; EventMappings remain tenant integrations. |
| Draft-save vs submit-only projection was debated; **current** behaviour projects save, submit, and delete. | **Verified** — do not “optimise” by dropping Saved events. |
| SignalR needs Azure SignalR under multiple replicas; Redis is not a backplane; sticky sessions are insufficient because bus consumers may run on another replica. | **Verified** — `ConfigureSignalR` + comments. |
| Dual ScanResult consumers (API + Web) are intentional. | **Verified**. |

## Product boundaries

| Lesson | Status |
|--------|--------|
| FlexForms is signed-in task-list applications, not anonymous public forms or third-party mailbox routing. | **Historical** product decision. |
| The External Applications → FlexForms rename left contract namespaces unchanged on purpose. | **Verified**. |
| Template Manager requires an acknowledgement before save (reporting impact). | **Verified** on Web. |
| FeatureManagement for Template Studio is Target **`Web`**. | **Verified**. |

## Delivery

| Lesson | Status |
|--------|--------|
| `main` is integration (Dev). Promote the **same** image Test→Prod. `production-build` vs `production` GitHub environments. | **Documented** in `docs/release-process.md`. |
| Trusted NuGet publishing is per repo; the nuget.org policy matches the reusable `pack-template.yml` `job_workflow_ref`. | **Historical** — check workflows. |
| Default code ownership is in `.github/CODEOWNERS`. | **Verified**. |

## Known gaps

Some areas were worked on without a written record that survives: the original permissions/role expansion analysis, collection-flow save behaviour, large JSON template limits, orphaned blob cleanup, and the scan-result cache design. Read the code before relying on assumptions in those areas, and add findings here.
