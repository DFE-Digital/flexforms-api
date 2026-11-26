using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.Controllers;

/// <summary>Shared set-up for the <c>/v1/admin/tenants</c> HTTP tests.</summary>
internal static class TenantAdminHttpTestKit
{
    internal const string AdminEmail = "alice@example.com";
    internal static readonly Guid TenantId = Guid.Parse(EaContextSeeder.TestTenantId);
    internal static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    internal static UpsertTenantSettingRequest Setting(string category, string json, string target = "Web", bool isSecret = false) =>
        new(category, target, ToBase64(json), isSecret);

    internal static string ToBase64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    internal static async Task SeedTenantAsync(CustomWebApplicationDbContextFactory<Program> factory) =>
        await WithTenantConfigAsync(factory, async db =>
        {
            db.Tenants.Add(new TenantEntity { Id = TenantId, Name = "Transfers" });
            return await db.SaveChangesAsync();
        });

    internal static async Task<T> WithTenantConfigAsync<T>(
        CustomWebApplicationDbContextFactory<Program> factory, Func<TenantConfigDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TenantConfigDbContext>());
    }

    internal static async Task<(int Status, string Message)> ApiErrorAsync(Func<Task> call)
    {
        var ex = await Assert.ThrowsAnyAsync<ExternalApplicationsException>(call);
        var message = ex is ExternalApplicationsException<ExceptionResponse> typed
            ? typed.Result?.Message ?? ""
            : ex.Response ?? "";
        return (ex.StatusCode, message);
    }

    internal static List<Claim> Admin() => [new(ClaimTypes.Email, AdminEmail), new(ClaimTypes.Role, RoleNames.Admin)];

    internal static List<Claim> SuperAdmin() => [new(ClaimTypes.Email, "root@example.com"), new(ClaimTypes.Role, RoleNames.SuperAdmin)];

    internal static void CallAs(CustomWebApplicationDbContextFactory<Program> factory, HttpClient httpClient, List<Claim> claims)
    {
        // CoreLibs reads TestClaims once, when the mock handler is first built; change the same list.
        factory.TestClaims ??= [];
        factory.TestClaims.Clear();
        factory.TestClaims.AddRange(claims);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user-token");
    }
}
