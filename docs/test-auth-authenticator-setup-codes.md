# Test Authentication: authenticator app setup codes created by hand

Normally an administrator creates a setup code in Web (**Admin → Users & Roles → User Manager → Create authenticator setup code**). That only works for the tenant they are signed in to. This runbook is for a SuperAdmin or platform engineer who needs a code for **another** tenant without signing in to it, as a temporary measure.

It writes a setup code straight into Redis. The tester then redeems it on that tenant's sign-in page, and the API saves the enrolment itself.

> Do not insert rows into `tenantconfig.TestAuthAuthenticators` by hand. The secret is encrypted with the API's Data Protection key ring (purpose `TestAuthAuthenticator.v1`), so a hand-written row cannot be read and the user is treated as not enrolled.

## Before you start

- You need write access to the environment's Redis instance. Anyone with that access can already change any cached data, so treat this as a privileged action.
- The environment must be Development, Local or Test, and running an API version that has setup codes.
- The target tenant must have `TestAuthentication:Enabled = true`. Your own tenant does not.
- You need the target tenant's id and the API's Redis database number (`CacheSettings:Redis:Database`, `0` unless the environment overrides it).
- Nothing is written to the access audit trail. Record who you created a code for, and why, in the ticket or change log.

## What the API expects

Source of truth: `Application/Security/TestAuthAuthenticatorInviteService.cs`, `Application/Common/TenantCacheKeyHelper.cs`, `Infrastructure/Caching/RedisAtomicCacheStore.cs`. If those change, update this page.

| Part | Value |
|------|-------|
| Key | `FlexForms:Cache:t:{tenantId}:TestAuthAuthenticatorInvite_{emailHash}` |
| `tenantId` | Tenant GUID, lower case, with hyphens |
| `emailHash` | SHA-256 of the email, trimmed and lower-cased, as **lower-case** hex |
| Value | `{"CodeHash":"{codeHash}","Id":"{id}"}` (property names are case-sensitive) |
| `codeHash` | SHA-256 of the code (upper-case letters and digits only), as **upper-case** hex |
| `id` | Any new GUID, 32 hex characters, no hyphens |
| Expiry | Up to 24 hours (`EX 86400`) |

**Always generate a new random code** with the script below. Never type in your own, reuse one, or copy an example from documentation: anyone who knows the code and the tester's email can redeem it first and enrol their own authenticator. The script uses the same alphabet and length as codes created in Web. When the code is redeemed, the API upper-cases it and ignores spaces and hyphens. As with codes created in Web, it works once, five wrong attempts discard it, and a new code for the same email replaces it.

## Steps

1. Build the key and value (works in Windows PowerShell 5.1 and PowerShell 7):

   ```powershell
   $tenantId = "00000000-0000-0000-0000-000000000000"   # target tenant id, lower case
   $email    = "tester@example.com".Trim().ToLowerInvariant()

   # Fresh random 10-character code. 32 symbols divide 256 exactly, so byte % 32 is unbiased.
   $alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
   $bytes    = New-Object byte[] 10
   [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
   $code     = -join ($bytes | ForEach-Object { $alphabet[$_ % 32] })

   $sha       = [System.Security.Cryptography.SHA256]::Create()
   $emailHash = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($email)) | ForEach-Object { $_.ToString("x2") })
   $codeHash  = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($code))  | ForEach-Object { $_.ToString("X2") })

   $key   = "FlexForms:Cache:t:${tenantId}:TestAuthAuthenticatorInvite_$emailHash"
   $value = '{"CodeHash":"' + $codeHash + '","Id":"' + [guid]::NewGuid().ToString("N") + '"}'
   $key
   $value
   "Code to give the tester: $($code.Substring(0, 5))-$($code.Substring(5))"
   ```

   Keep `${tenantId}` in braces: PowerShell would read `$tenantId:` as a scoped variable and drop it from the key.

2. Write it to Redis (`redis-cli`, or the Azure Cache for Redis console), using the API's database number:

   ```text
   SELECT 0
   SET "<key>" '<value>' EX 86400
   ```

3. Give the tester the code the script printed, directly to them only, and do not keep a copy. On the target tenant's sign-in page they choose **I have an authenticator app setup code**, enter their email and the code, scan the QR code and enter the code from the app. That signs them in and saves the enrolment.

## If it does not work

- **"The setup code is incorrect, has expired or has already been used"**: check that the email was lower-cased before hashing, that the email hash is lower-case hex and the code hash is upper-case hex, that the tenant id is lower case, and that you wrote to the API's Redis database. Five wrong attempts also discard the code; run the script again for a new one.
- **"Test authentication is not enabled for this service"**: the target tenant does not have Test Authentication enabled, or the environment is Production.
