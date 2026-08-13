using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

public class TenantAuthPrincipalFactoryTests
{
    [Fact]
    public void BuildPrincipal_EmitsTenantIdAuthProviderIsServiceAndRoles()
    {
        var tenantId = Guid.NewGuid();
        var provider = new TenantAuthProvider(
            TenantId: tenantId,
            Name: "svc",
            Kind: TenantAuthProviderKind.ApiKey,
            IsServicePrincipal: true,
            Roles: new[] { "ServiceCaller", "Admin" });

        var principal = TenantAuthPrincipalFactory.BuildPrincipal(provider, AuthConstants.ApiKey);

        Assert.Equal(tenantId.ToString(), principal.FindFirst(TenantAuthClaimTypes.TenantId)!.Value);
        Assert.Equal("svc", principal.FindFirst(TenantAuthClaimTypes.AuthProvider)!.Value);
        Assert.Equal("true", principal.FindFirst(TenantAuthClaimTypes.IsService)!.Value);
        Assert.True(principal.IsInRole("ServiceCaller"));
        Assert.True(principal.IsInRole("Admin"));
        Assert.Equal(AuthConstants.ApiKey, principal.Identity!.AuthenticationType);
        Assert.DoesNotContain(principal.Claims, c => c.Type == "permission");
    }

    [Fact]
    public void BuildPrincipal_EmitsFileValidationWrite_WhenServiceRoleIsFileValidation()
    {
        var provider = new TenantAuthProvider(
            TenantId: Guid.NewGuid(),
            Name: "excel-fn",
            Kind: TenantAuthProviderKind.ApiKey,
            IsServicePrincipal: true,
            Roles: new[] { "FileValidation" });

        var principal = TenantAuthPrincipalFactory.BuildPrincipal(provider, AuthConstants.ApiKey);

        Assert.Contains(
            principal.Claims,
            c => c.Type == "permission" && c.Value == "FileValidation:Any:Write");
    }

    [Fact]
    public void BuildPrincipal_MergesAdditionalClaims()
    {
        var provider = new TenantAuthProvider(
            TenantId: Guid.NewGuid(),
            Name: "mtls",
            Kind: TenantAuthProviderKind.Mtls,
            IsServicePrincipal: true);

        var extra = new[] { new Claim(ClaimTypes.SerialNumber, "ABC123") };

        var principal = TenantAuthPrincipalFactory.BuildPrincipal(provider, AuthConstants.Mtls, extra);

        Assert.Equal("ABC123", principal.FindFirst(ClaimTypes.SerialNumber)!.Value);
    }

    [Fact]
    public void StashProvider_StoresProviderUnderAuthConstantsKey()
    {
        var http = new DefaultHttpContext();
        var provider = new TenantAuthProvider(
            TenantId: Guid.NewGuid(),
            Name: "x",
            Kind: TenantAuthProviderKind.ApiKey,
            IsServicePrincipal: true);

        TenantAuthPrincipalFactory.StashProvider(http, provider);

        Assert.Same(provider, http.Items[AuthConstants.MatchedAuthProviderKey]);
    }
}
