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

Packages are **independently versioned**. Current FlexForms pins (nuget.org latest at last bump): Contracts `2.0.6`, Security `2.0.2`, Caching/Http/AsyncProcessing/Utilities/Testing `2.0.0`, Email/FileStorage/Notifications/Messaging.* `1.0.0`. Contracts 2.x is net10.0 only, which is why Api.Client no longer targets net8.0. Bumping CoreLibs also requires matching `Microsoft.Extensions.*` / AspNetCore / `Microsoft.Identity.Web` / JWT pins at **10.0.15** / **4.18.0** / **8.23.0** or NU1605 package-downgrade restore fails.

Contracts after 2.0.2 adds `AddContributorRequest.Permissions` (`ContributorInvitePermissions`) for invite-time Application / ApplicationFiles grants. Contracts **2.0.4** adds `UserAuthorizationDto.InviteScopedApplicationKeys` for invite-scope claim reconstruction. Contracts **2.0.6** adds `VerifyTestAuthPasswordResponse.IsLockedOut` / `IsPermanentLockout` / `RetryAfterSeconds` and the Test Auth sign-in lock DTOs (`TestAuthSignInLockStatusResponse`, `SetTestAuthSignInLockRequest`). Bump Domain + Api.Client together, then Web.

**Do not** replace NuGet with local `ProjectReference` to `DfE.CoreLibs` in normal work. Local assemblies version as `1.0.0.0`; published `GovUK.Dfe.CoreLibs.Notifications` may demand `Contracts 1.0.x.0` → `FileLoadException` at startup. **Verified** in README; **Historical** from Prism development.

If you must use project refs for an unpublished contract, replace **every** CoreLibs package in the graph that depends on that assembly, then revert before merge.

Changing a DTO in `GovUK.Dfe.CoreLibs.Contracts.ExternalApplications` requires: publish CoreLibs → bump API Domain/Client → regenerate NSwag if needed → bump Web/Prism package refs.

## Api.Client

Targets net10.0 only. NSwag regenerates on local build. CI packs `GovUK.Dfe.FlexForms.Api.Client` with GitVersion (`GitVersion.Packages.yml`). Publishing uses NuGet trusted publishing (OIDC), same pattern as CoreLibs. Trusted publishing is **per GitHub repo**.

Do **not** add API-key auth to Api.Client — it is the first-party Web client. Integrations use a narrow `HttpClient`.

## Other NuGet

- `Otp.NET` (Application): RFC 6238 TOTP for Test Authentication authenticator apps (`TestAuthAuthenticatorService`).

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
