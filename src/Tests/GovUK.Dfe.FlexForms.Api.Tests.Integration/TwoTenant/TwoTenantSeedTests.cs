using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant;

/// <summary>
/// Pins what <see cref="TwoTenantSeeder"/> gives later tests: every seeded person can sign in to their tenant
/// through token exchange with the expected role, and the custom roles' grants reach the permission claims.
/// </summary>
public sealed class TwoTenantSeedTests(TwoTenantApi api) : IClassFixture<TwoTenantApi>
{
    public static TheoryData<string, string, string> Members()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var tenant in new[] { TestTenant.A, TestTenant.B })
        {
            data.Add(tenant.Key, tenant.OwnerEmail, RoleNames.User);
            data.Add(tenant.Key, tenant.AdminEmail, RoleNames.Admin);
            data.Add(tenant.Key, tenant.CaseworkerEmail, "Caseworker");
            data.Add(tenant.Key, tenant.TemplateManagerEmail, "TemplateManager");
            data.Add(tenant.Key, tenant.UserManagerEmail, "UserManager");
            data.Add(tenant.Key, tenant.MemberEmail, RoleNames.User);
            data.Add(tenant.Key, tenant.ScopedTemplateManagerEmail, RoleNames.User);
            data.Add(tenant.Key, TwoTenantSeeder.SuperAdminEmail, RoleNames.SuperAdmin);
        }

        data.Add(TestTenant.A.Key, TwoTenantSeeder.BothTenantsEmail, RoleNames.Admin);
        data.Add(TestTenant.B.Key, TwoTenantSeeder.BothTenantsEmail, RoleNames.User);
        return data;
    }

    [Theory]
    [MemberData(nameof(Members))]
    public async Task EverySeededPersonSignsInWithTheirTenantRole(string tenantKey, string email, string role)
    {
        var tenant = tenantKey == TestTenant.A.Key ? TestTenant.A : TestTenant.B;

        var token = await api.SignInAsync(tenant, email);

        var roles = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Where(c => c.Type is "role" or ClaimTypes.Role)
            .Select(c => c.Value);
        Assert.Equal([role], roles);
    }

    [Theory]
    [InlineData("form.manager", "Template:{template}:Manage")]
    [InlineData("member", "Template:{template}:Write")]
    [InlineData("member", "User:{email}:Read")]
    [InlineData("caseworker", "Application:Any:Read")]
    [InlineData("caseworker", "ApplicationFiles:Any:Read")]
    [InlineData("template.manager", "Template:Any:Manage")]
    [InlineData("user.manager", "User:Any:Manage")]
    public async Task CustomRoleGrantsReachThePermissionClaims(string person, string permission)
    {
        foreach (var tenant in new[] { TestTenant.A, TestTenant.B })
        {
            var email = $"{person}@{tenant.Key}.example";
            var token = await api.SignInAsync(tenant, email);
            using var response = await api.Client(tenant, token).GetAsync("v1/me/permissions");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(
                permission.Replace("{template}", tenant.TemplateId.ToString()).Replace("{email}", email),
                await PermissionsAsync(response));
        }
    }

    [Fact]
    public async Task TheOwnerHoldsGrantsOnTheirOwnApplicationOnly()
    {
        foreach (var (tenant, other) in new[] { (TestTenant.A, TestTenant.B), (TestTenant.B, TestTenant.A) })
        {
            var token = await api.SignInAsync(tenant, tenant.OwnerEmail);
            using var response = await api.Client(tenant, token).GetAsync("v1/me/permissions");
            var permissions = await PermissionsAsync(response);

            Assert.Contains($"Application:{tenant.ApplicationId}:Write", permissions);
            Assert.Contains($"ApplicationFiles:{tenant.ApplicationId}:Delete", permissions);
            Assert.DoesNotContain(permissions, p => p.Contains(other.ApplicationId.ToString(), StringComparison.Ordinal));
        }
    }

    private static async Task<IReadOnlyList<string>> PermissionsAsync(HttpResponseMessage response)
    {
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("permissions").EnumerateArray()
            .Select(p => $"{p.GetProperty("resourceType").GetString()}:{p.GetProperty("resourceKey").GetString()}:{p.GetProperty("accessType").GetString()}")
            .ToList();
    }
}
