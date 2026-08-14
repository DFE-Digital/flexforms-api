using System.Security.Claims;
using Xunit;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.Handlers;

public class FileValidationPermissionHandlerTests
{
    [Fact]
    public async Task Succeeds_ForServicePrincipalWithFileValidationWrite()
    {
        var http = new DefaultHttpContext();
        TenantAuthPrincipalFactory.StashProvider(http, new TenantAuthProvider(
            Guid.NewGuid(), "fn", TenantAuthProviderKind.ApiKey, true, Roles: ["FileValidation"]));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);

        var principal = TenantAuthPrincipalFactory.BuildPrincipal(
            (TenantAuthProvider)http.Items[AuthConstants.MatchedAuthProviderKey]!,
            AuthConstants.ApiKey);

        var context = new AuthorizationHandlerContext(
            [new FileValidationPermissionRequirement()],
            principal,
            null);

        await new FileValidationPermissionHandler(accessor).HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_ForInteractiveAdminWithoutGrant()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Email, "a@b.com")],
            "Test"));

        var context = new AuthorizationHandlerContext(
            [new FileValidationPermissionRequirement()],
            principal,
            null);

        await new FileValidationPermissionHandler(accessor).HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
