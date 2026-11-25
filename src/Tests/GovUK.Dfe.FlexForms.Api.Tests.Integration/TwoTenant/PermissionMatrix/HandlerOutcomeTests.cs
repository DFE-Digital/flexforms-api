namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant.PermissionMatrix;

/// <summary>
/// Layer B of the permission matrix: real requests to the handler-gated TenantAdmin, Users, Roles and Templates
/// endpoints, from every principal the policy lets through (taken from <see cref="PermissionMatrix"/>), and the
/// status each gets back. Its own <see cref="MatrixApi"/>, so these writes never meet the Layer A tests.
/// </summary>
public sealed class HandlerOutcomeTests(MatrixApi api) : IClassFixture<MatrixApi>
{
    public static TheoryData<string> Cases() => new(HandlerCases.All.Select(c => c.Name));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EachPrincipalPastThePolicyGetsTheHandlersAnswer(string name)
    {
        var handlerCase = HandlerCases.All.Single(c => c.Name == name);
        var row = PermissionMatrix.Endpoints.Single(e => e.Key == handlerCase.Key);
        var callers = HandlerCases.Order.Where(p => row.Allowed.HasFlag(p)).ToList();

        var actual = new Dictionary<Principal, int>();
        foreach (var principal in callers)
        {
            using var client = api.For(principal);
            using var request = await handlerCase.Build(api, principal);
            using var response = await client.SendAsync(request);
            actual[principal] = (int)response.StatusCode;
        }

        var described = string.Join(", ", actual.Select(a => $"(Principal.{a.Key}, {a.Value})"));
        Assert.True(
            callers.ToHashSet().SetEquals(handlerCase.Expected.Keys) && callers.All(p => handlerCase.Expected[p] == actual[p]),
            $"{name}: got Codes({described})");
    }
}
