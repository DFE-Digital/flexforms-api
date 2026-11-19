using System;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Stable identifiers for token exchange failures that consumers (FlexForms Web) handle specially.
/// </summary>
public static class TokenExchangeErrors
{
    /// <summary>
    /// <c>ErrorId</c> on the synthetic 403 returned when the signed-in user has no active
    /// membership in the request's tenant. Web matches on this value; do not change it
    /// without updating flexforms-web.
    /// </summary>
    public const string NoTenantMembershipErrorId = "Exchange-403-NoTenantMembership";

    /// <summary>
    /// User-facing message on the synthetic 403 for <see cref="NoTenantMembershipErrorId"/>.
    /// Must not contain "token", "expired" or "unauthorized": Web treats those words as an
    /// authentication failure and signs the user out instead of showing the reason.
    /// </summary>
    public const string NoTenantMembershipMessage =
        "You are not authorised to access this service. If you think you should have access, contact your administrator.";

    /// <summary>
    /// Text in the API's <c>POST /v1/tokens/exchange</c> 403 body when the user is not a member of
    /// the tenant (<c>ExchangeTokenQueryHandler</c>). Already-deployed clients match on it, so the
    /// API message must keep this phrase.
    /// </summary>
    public const string NotTenantMemberMarker = "not a member of tenant";

    /// <summary>
    /// True when an exchange response body is the API's "not a member of tenant" denial.
    /// </summary>
    public static bool IsNotTenantMemberResponse(string? responseBody) =>
        !string.IsNullOrEmpty(responseBody)
        && responseBody.Contains(NotTenantMemberMarker, StringComparison.OrdinalIgnoreCase);
}
