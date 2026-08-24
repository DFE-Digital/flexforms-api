# TenantConfig import scripts (Path 3 — migration / bootstrap only)
#
# These scripts seed TenantConfig from JSON dumps or legacy sources.
# Runtime Web/API artefacts do not load per-app appsettings folders.
#
# Important: ApplicationTemplates:HostMappings (Api) and Template:HostMappings (Web)
# must list ONLY claimable template GUIDs (TenantId null legacy, or owned by that tenant).
# Cross-tenant GUIDs are rejected on SuperAdmin save and ignored at runtime.
# On shared EA databases, audit with Audit-TenantHostMappings.sql and fix with Fix-TenantHostMappings.sql.
#
# Prerequisites
# - Windows PowerShell 5.1+ or PowerShell 7+
# - API running (for upserts)
# - Bearer token must be an exchanged *user* JWT for an Admin user (not Entra
#   client-credentials / ServiceCallers). Machine tokens are rejected.
# - X-Tenant-ID (or matching Origin) MUST equal the tenantId being upserted
#   (tenant admins cannot write another tenant's settings)
#
# -----------------------------------------------------------------------------
# Web settings (Target=Web) — Import-WebTenantConfig.ps1
# -----------------------------------------------------------------------------
# Preferred: -TenantSettingsFile pointing at a JSON dump of the tenant's Web settings.
# Optional legacy: configurations/{App}/appsettings*.json if that folder still exists.
# Always merges the Web user-secrets application section when present.
#
# Dry run:
#   powershell -File ./Import-WebTenantConfig.ps1 -DryRun -TenantSettingsFile .\transfers-web.json
#
# Transfers:
#   powershell -File ./Import-WebTenantConfig.ps1 -AccessToken $token -TenantSettingsFile .\transfers-web.json
#
# LSRP:
#   powershell -File ./Import-WebTenantConfig.ps1 -ApplicationName Lsrp `
#     -TenantId "22222222-2222-4222-8222-222222222222" `
#     -Hostname "lsrp.localhost" `
#     -FrontendOrigin "https://lsrp.localhost:7020" `
#     -TenantSettingsFile .\lsrp-web.json `
#     -AccessToken $token `
#     -SqlConnectionString "Server=localhost,1433;Database=TenantConfig;User Id=SA;Password=YourPassword123!;TrustServerCertificate=True;"
#
# -----------------------------------------------------------------------------
# API settings (Target=Api) — Import-ApiTenantConfig.ps1
# -----------------------------------------------------------------------------
# Preferred: -TenantSettingsFile (tenant object or { "Tenants": { "Lsrp": { ... } } }).
# Optional legacy: Tenants:{App} from API user secrets / old appsettings dumps
# (API appsettings no longer ships Tenants sections for runtime).
#
# Dry run:
#   powershell -File ./Import-ApiTenantConfig.ps1 -ApplicationName Lsrp -DryRun
#
# LSRP API settings:
#   powershell -File ./Import-ApiTenantConfig.ps1 -ApplicationName Lsrp -AccessToken $token `
#     -SqlConnectionString "Server=localhost,1433;Database=TenantConfig;User Id=SA;Password=YourPassword123!;TrustServerCertificate=True;" `
#     -Hostname "lsrp.localhost" `
#     -FrontendOrigin "https://lsrp.localhost:7020"
#
# Optional JSON file:
#   powershell -File ./Import-ApiTenantConfig.ps1 -ApplicationName Lsrp `
#     -TenantSettingsFile ".\lsrp-api-tenant.json" -AccessToken $token
#
# -----------------------------------------------------------------------------
# After either import — refresh API cache
# -----------------------------------------------------------------------------
#   Invoke-RestMethod -Method Post -Uri "https://localhost:7089/v1/admin/tenants/refresh" `
#     -Headers @{
#       Authorization = "Bearer $token"
#       "X-Tenant-ID" = "22222222-2222-4222-8222-222222222222"
#     }
#
# Verify:
#   SELECT Category, Target FROM tenantconfig.TenantSettings
#   WHERE TenantId = '22222222-2222-4222-8222-222222222222'
#   ORDER BY Target, Category;
