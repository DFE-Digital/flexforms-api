using System.Security.Claims;
using System.Text;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin;

/// <summary>Shared values for the tenant admin handler tests.</summary>
internal static class TenantAdminTestData
{
    public static readonly Guid OwnTenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public const string ActorEmail = "admin@example.com";

    public static TenantConfiguration Tenant(
        Guid? id = null,
        string name = "Transfers",
        IDictionary<string, string?>? settings = null,
        string[]? frontendOrigins = null) =>
        new(
            id ?? OwnTenantId,
            name,
            new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build(),
            frontendOrigins ?? []);

    public static string ToBase64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    public static IHttpContextAccessor HttpContextFor(params Claim[] claims)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        });
        return accessor;
    }

    public static IHttpContextAccessor SignedInAs(string email = ActorEmail) =>
        HttpContextFor(new Claim(ClaimTypes.Email, email));

    public static TenantSettingRow Row(string category, string target, string json, bool isSecret = false) =>
        new(Guid.NewGuid(), category, target, json, isSecret, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public static TheoryData<string> SuperAdminOnlyCategories() => new(SuperAdminOnlyTenantSettingCategories.All);
}
