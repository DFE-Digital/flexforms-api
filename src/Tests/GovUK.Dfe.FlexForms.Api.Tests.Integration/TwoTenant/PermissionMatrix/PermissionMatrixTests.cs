using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant.PermissionMatrix;

/// <summary>
/// Layer A of the permission matrix: for every controller action and every <see cref="Principal"/>, whether
/// the endpoint's authorization policy lets the caller through. Requests run in policy-only mode, so an
/// allowed request stops after authorization and changes nothing. The inventory tests make sure no endpoint
/// can be added without a row.
/// </summary>
public sealed class PermissionMatrixTests(MatrixApi api) : IClassFixture<MatrixApi>
{
    private static readonly string[] NonControllerEndpoints =
        ["/health", "/healthz", "/liveness", "/readiness", "/hubs/notifications", "/hubs/notifications/negotiate"];

    public static TheoryData<string> Rows() => new(PermissionMatrix.Endpoints.Select(e => e.Key));

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task ThePolicyLetsThroughExactlyTheExpectedPrincipals(string key)
    {
        var row = PermissionMatrix.Endpoints.Single(e => e.Key == key);
        var mismatches = new List<string>();

        foreach (var principal in MatrixApi.All)
        {
            var (outcome, status) = await SendPolicyOnlyAsync(row, principal);

            if (row.Open)
            {
                if (outcome != PolicyOutcome.NotEvaluated || status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.UnsupportedMediaType)
                    mismatches.Add($"{principal}: expected an open endpoint, got {outcome} ({(int)status})");
                continue;
            }

            var expected = row.Allowed.HasFlag(principal)
                ? PolicyOutcome.Allowed
                : principal == Principal.Anonymous ? PolicyOutcome.Challenged : PolicyOutcome.Forbidden;
            var acceptable = expected == PolicyOutcome.Forbidden
                ? outcome is PolicyOutcome.Forbidden or PolicyOutcome.Challenged
                : outcome == expected;
            if (!acceptable)
                mismatches.Add($"{principal}: expected {expected}, got {outcome} ({(int)status})");
        }

        Assert.True(mismatches.Count == 0, $"{key}:\n  {string.Join("\n  ", mismatches)}");
    }

    [Fact]
    public void EveryControllerActionHasExactlyOneRow()
    {
        var actions = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Select(Key)
            .ToHashSet();
        var rows = PermissionMatrix.Endpoints.Select(e => e.Key).ToList();

        Assert.Equal(rows.Count, rows.Distinct().Count());
        Assert.Empty(actions.Except(rows));
        Assert.Empty(rows.Except(actions));
    }

    [Fact]
    public void EveryControllerActionInTheApiAssemblyHasARow()
    {
        var reflected = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToHashSet();

        Assert.Empty(reflected.Except(PermissionMatrix.Endpoints.Select(e => e.Key)));
    }

    [Fact]
    public void TheRowsMatchExpectedSecurityJson()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(ExpectedSecurityPath()));
        var expected = json.RootElement.GetProperty("Endpoints").EnumerateArray()
            .ToDictionary(
                e => $"{e.GetProperty("Controller").GetString()}.{e.GetProperty("Action").GetString()}",
                e => e.GetProperty("ExpectedSecurity").GetString());

        Assert.Equal(expected.Keys.Order(), PermissionMatrix.Endpoints.Select(e => e.Key).Order());
        Assert.Equal(
            expected.Where(e => e.Value == "AllowAnonymous").Select(e => e.Key).Order(),
            PermissionMatrix.Endpoints.Where(e => e.Open).Select(e => e.Key).Order());
    }

    [Fact]
    public void TheOnlyRoutedEndpointsOutsideControllersAreHealthAndTheHub()
    {
        var others = api.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'));

        Assert.Equal(NonControllerEndpoints.Order(), others.Order());
    }

    private async Task<(PolicyOutcome Outcome, HttpStatusCode Status)> SendPolicyOnlyAsync(MatrixEndpoint row, Principal principal)
    {
        using var client = api.For(principal);
        using var request = row.ToRequest();
        var id = Guid.NewGuid().ToString();
        request.Headers.Add(PolicyRecorder.RequestIdHeader, id);
        request.Headers.Add(PolicyRecorder.PolicyOnlyHeader, "true");
        using var response = await client.SendAsync(request);
        return (api.Outcomes.Take(id), response.StatusCode);
    }

    private static string Key(ControllerActionDescriptor action) => $"{action.ControllerTypeInfo.Name}.{action.MethodInfo.Name}";

    private static string ExpectedSecurityPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "Tests", "GovUK.Dfe.FlexForms.Api.Tests", "Security", "ExpectedSecurity.json");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("ExpectedSecurity.json not found above the test output folder.");
    }
}
