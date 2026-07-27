using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Matches ID-token audiences to the resolved tenant's DfESignIn / Entra SSO client ids.
/// </summary>
public sealed class TenantOidcAudienceBinder : ITenantOidcAudienceBinder
{
    public bool TokenMatchesTenant(TenantConfiguration tenant, IEnumerable<string> tokenAudiences)
    {
        var configured = GetConfiguredAudiences(tenant);
        if (configured.Count == 0)
        {
            // Local/test tenants often have no OIDC client ids — do not block exchange.
            return true;
        }

        var tokenAudList = tokenAudiences as IReadOnlyList<string> ?? tokenAudiences.ToList();
        if (tokenAudList.Count == 0)
            return false;

        foreach (var aud in tokenAudList)
        {
            if (string.IsNullOrWhiteSpace(aud))
                continue;

            foreach (var configuredAud in configured)
            {
                if (string.Equals(aud, configuredAud, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(aud, $"api://{configuredAud}", StringComparison.OrdinalIgnoreCase)
                    || string.Equals($"api://{aud}", configuredAud, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public IReadOnlyCollection<string> GetConfiguredAudiences(TenantConfiguration tenant)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dfeClientId = tenant.Settings["DfESignIn:ClientId"];
        if (!string.IsNullOrWhiteSpace(dfeClientId))
            result.Add(dfeClientId.Trim());

        var entraClientId = tenant.Settings["EntraSso:ClientId"];
        if (!string.IsNullOrWhiteSpace(entraClientId))
            result.Add(entraClientId.Trim());

        var azureAdClientId = tenant.Settings["AzureAd:ClientId"];
        if (!string.IsNullOrWhiteSpace(azureAdClientId))
            result.Add(azureAdClientId.Trim());

        var azureAdAudience = tenant.Settings["AzureAd:Audience"];
        if (!string.IsNullOrWhiteSpace(azureAdAudience))
            result.Add(azureAdAudience.Trim());

        return result.ToList();
    }
}
