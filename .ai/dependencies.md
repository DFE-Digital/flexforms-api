# Dependencies

## Other repositories

| Repo | Coupling |
|------|----------|
| flexforms-web | Primary client of REST, SignalR, Api.Client, TenantConfig Target `Web` |
| flexforms-prism | Consumes outbox events + `IInternalPrismClient`; API calls Prism export-policy HTTP |
| DfE.CoreLibs | NuGet: Contracts, Security, Http, Caching, Email, FileStorage, Notifications, AsyncProcessing, Messaging.*, Utilities, Testing |
| rsd-file-scanner-function | Scan worker |
| rsd-clamav-api | Scan engine (indirect) |

Missing from this workspace (do not invent): tenant file-validation Function repos; a shared `flexforms-knowledge` repo; scanner Function App Terraform (not in the scanner repo).

## CoreLibs versioning (critical)

Packages are **independently versioned**. FlexForms pins different versions per project (e.g. Contracts `1.0.113`, Security `1.1.32`, Messaging `0.1.6`).

**Do not** replace NuGet with local `ProjectReference` to `DfE.CoreLibs` in normal work. Local assemblies version as `1.0.0.0`; published `GovUK.Dfe.CoreLibs.Notifications` may demand `Contracts 1.0.x.0` → `FileLoadException` at startup. **Verified** in README; **Historical** from Prism development.

If you must use project refs for an unpublished contract, replace **every** CoreLibs package in the graph that depends on that assembly, then revert before merge.

Changing a DTO in `GovUK.Dfe.CoreLibs.Contracts.ExternalApplications` requires: publish CoreLibs → bump API Domain/Client → regenerate NSwag if needed → bump Web/Prism package refs.

## Api.Client

NSwag regenerates on local build. CI packs `GovUK.Dfe.FlexForms.Api.Client` with GitVersion (`GitVersion.Packages.yml`). Publishing uses NuGet trusted publishing (OIDC), same pattern as CoreLibs. Trusted publishing is **per GitHub repo**.

Do **not** add API-key auth to Api.Client — it is the first-party Web client. Integrations use a narrow `HttpClient`.

## Hosting

- Terraform: `terraform/` → DfE `terraform-azurerm-container-apps-hosting`.
- GitHub Actions: `dotnet-ci.yml`, `test-solution.yml`, `deploy.yml`, `release.yml`, `cut-release.yml`, `ci-pack-api-client.yml`.
- **Release Flow:** `main` → development; `release/**` → test RC; production from **tagged images without rebuild**. See `docs/release-process.md`.
- GitHub environments: `production-build` (build/import) vs `production` (deploy approval). OIDC is environment-scoped; secrets must exist on both.

## Application Insights

Pinned to 2.x because Serilog sink is incompatible with AI 3.x (`Api.csproj` comment). **Historical:** bumping `Microsoft.ApplicationInsights.AspNetCore` to 3.1.2 removed `ApplicationInsightsLoggerProvider` and broke runtime.

## MassTransit

Register a custom `IEventPublisher` **after** `AddDfEMassTransit` or MassTransit’s publisher wins.

`SkipMassTransit` / `SkipBackgroundService` exist for tests and NSwag codegen. CoreLibs `AsyncProcessing` hosted services have historically broken integration test factories.
