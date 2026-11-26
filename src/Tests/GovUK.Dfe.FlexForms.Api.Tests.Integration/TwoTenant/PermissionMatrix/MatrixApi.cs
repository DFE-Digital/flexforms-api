using System.Net.Http.Json;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant.PermissionMatrix;

/// <summary>Who calls an endpoint in the permission matrix. All are tenant A callers.</summary>
[Flags]
public enum Principal
{
    None = 0,
    Anonymous = 1 << 0,

    /// <summary>A plain member with what self-registration grants (seeded through <c>UserFactory.CreateUser</c>).</summary>
    User = 1 << 1,

    /// <summary>Added to the owner's application through <c>POST …/contributors</c>.</summary>
    Contributor = 1 << 2,

    /// <summary>Created tenant A's seeded application.</summary>
    Owner = 1 << 3,
    Caseworker = 1 << 4,

    /// <summary>A member whose only template grant is Manage on tenant A's form.</summary>
    FormManager = 1 << 5,
    TemplateManager = 1 << 12,
    UserManager = 1 << 6,
    Admin = 1 << 7,
    SuperAdmin = 1 << 8,

    /// <summary>The service principal API key Web uses (no roles).</summary>
    Service = 1 << 9,

    /// <summary>A service principal API key whose provider carries the Admin role.</summary>
    ServiceAdmin = 1 << 10,

    /// <summary>The file-validation service (a service principal with the FileValidation role).</summary>
    FileValidation = 1 << 11,

    Users = User | Contributor | Owner | Caseworker | FormManager | TemplateManager | UserManager | Admin | SuperAdmin,
    Services = Service | ServiceAdmin | FileValidation,
    Authenticated = Users | Services,
    Everyone = Anonymous | Authenticated,
}

/// <summary>
/// <see cref="TwoTenantApi"/> with the <see cref="PolicyRecorder"/>, an offline <c>PlatformBearer</c>, and a
/// credential for every <see cref="Principal"/>. The contributor is added through the API when the fixture
/// starts, so it holds what production grants.
/// </summary>
public sealed class MatrixApi : TwoTenantApi, IAsyncLifetime
{
    public const string ContributorEmail = "contributor@alpha.example";

    private readonly Dictionary<Principal, string> _tokens = [];

    public PolicyOutcomes Outcomes { get; } = new();

    public static IEnumerable<Principal> All =>
        Enum.GetValues<Principal>().Where(p => p != Principal.None && (p & (p - 1)) == 0);

    public HttpClient For(Principal principal)
    {
        var a = TestTenant.A;
        return principal switch
        {
            Principal.Anonymous => Client(a),
            Principal.Service => Client(a, apiKey: a.ServiceApiKey),
            Principal.ServiceAdmin => Client(a, apiKey: a.ServiceAdminApiKey),
            Principal.FileValidation => Client(a, apiKey: a.FileValidationApiKey),
            _ => Client(a, _tokens[principal]),
        };
    }

    public async Task InitializeAsync()
    {
        var a = TestTenant.A;
        _tokens[Principal.Owner] = await SignInAsync(a, a.OwnerEmail);
        using (var owner = Client(a, _tokens[Principal.Owner]))
        {
            using var added = await owner.PostAsJsonAsync(
                $"v1/applications/{a.ApplicationId}/contributors",
                new { Name = "Matrix Contributor", Email = ContributorEmail });
            await EnsureSuccessAsync(added, "add the matrix contributor");
        }

        _tokens[Principal.User] = await SignInAsync(a, a.MemberEmail);
        _tokens[Principal.Contributor] = await SignInAsync(a, ContributorEmail);
        _tokens[Principal.Caseworker] = await SignInAsync(a, a.CaseworkerEmail);
        _tokens[Principal.FormManager] = await SignInAsync(a, a.ScopedTemplateManagerEmail);
        _tokens[Principal.TemplateManager] = await SignInAsync(a, a.TemplateManagerEmail);
        _tokens[Principal.UserManager] = await SignInAsync(a, a.UserManagerEmail);
        _tokens[Principal.Admin] = await SignInAsync(a, a.AdminEmail);
        _tokens[Principal.SuperAdmin] = await SignInAsync(a, TwoTenantSeeder.SuperAdminEmail);

        // Tenant A in the tenant-config store, with one secret setting, for the TenantAdmin endpoints.
        using (var scope = Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<TenantConfigDbContext>();
            store.Tenants.Add(new TenantEntity { Id = a.Id, Name = a.Name });
            await store.SaveChangesAsync();
        }

        using (var superAdmin = For(Principal.SuperAdmin))
        {
            using var secret = await superAdmin.PostAsJsonAsync($"v1/admin/tenants/{a.Id}/settings", new
            {
                Category = "Layout",
                Target = "Web",
                SettingsJson = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"Auth":{"ClientSecret":"matrix-secret"}}""")),
                IsSecret = true,
            });
            await EnsureSuccessAsync(secret, "store tenant A's secret setting");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string step)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Could not {step}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton(Outcomes);
        // The framework registers a default handler and the API adds its own after it; the last one is used.
        var real = services.Last(d => d.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));
        services.RemoveAll<IAuthorizationMiddlewareResultHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(sp => new PolicyRecorder(
            (IAuthorizationMiddlewareResultHandler)ActivatorUtilities.CreateInstance(sp, real.ImplementationType!),
            sp.GetRequiredService<PolicyOutcomes>()));

        // The real PlatformBearer (the platform Entra app from appsettings.json) stays, but with an empty
        // configuration so validating a token never fetches Microsoft's signing keys over the network.
        services.PostConfigure<JwtBearerOptions>(AuthConstants.PlatformBearer, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration();
            options.ConfigurationManager = null;
        });
    }
}
