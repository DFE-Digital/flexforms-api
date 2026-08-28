using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using GovUK.Dfe.CoreLibs.Http.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Controllers
{
    /// <summary>
    /// Controller for SignalR hub authentication.
    /// <para>
    /// Both endpoints run AFTER <c>TenantResolutionMiddleware</c>, so the current tenant
    /// is always available via <see cref="ITenantContextAccessor"/>. The ticket cache key
    /// is tenant-scoped (defense-in-depth) so a ticket minted under tenant A cannot be
    /// redeemed against tenant B even if the cache (Redis) is shared and the GUID were
    /// ever leaked.
    /// </para>
    /// </summary>
    public class HubAuthController(
        IDistributedCache cache,
        ITenantContextAccessor tenantContextAccessor) : ControllerBase
    {
        //Create a single use ticket for the hub, which is valid for 1 minute
        [HttpPost("auth/hub-ticket")]
        [SwaggerResponse(200, "The created ticket.", typeof(Dictionary<string, string>))]
        [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
        [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
        [SwaggerResponse(403, "Forbidden - user does not have required permissions", typeof(ExceptionResponse))]
        [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
        [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
        [Authorize(AuthenticationSchemes = AuthConstants.TenantBearer)]
        public async Task<IActionResult> CreateHubTicket(CancellationToken ct)
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrWhiteSpace(email))
                return Forbid();

            var tenantId = tenantContextAccessor.CurrentTenant?.Id
                ?? throw new InvalidOperationException("Tenant context is required to create a hub ticket.");

            var ticket = Guid.NewGuid().ToString("N");
            await cache.SetStringAsync(BuildTicketKey(tenantId, ticket), email,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                }, ct);

            var redeemUrl = Url.ActionLink(
                action: nameof(RedeemHubCookie),
                controller: "HubAuth",
                values: new { ticket },
                protocol: Request.Scheme);

            return Ok(new { url = redeemUrl });
        }

        // Redeem and validate the ticket to create a cookie for the hub
        [HttpGet("auth/hub-cookie")]
        [SwaggerResponse(204)]
        [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
        [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
        [SwaggerResponse(403, "Forbidden - user does not have required permissions", typeof(ExceptionResponse))]
        [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
        [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
        [AllowAnonymous]
        public async Task<IActionResult> RedeemHubCookie([FromQuery] string ticket, CancellationToken ct)
        {
            var tenantId = tenantContextAccessor.CurrentTenant?.Id;
            if (tenantId is null)
                return Unauthorized();

            var key = BuildTicketKey(tenantId.Value, ticket);
            var email = await cache.GetStringAsync(key, ct);
            if (string.IsNullOrEmpty(email)) return Unauthorized();
            await cache.RemoveAsync(key, ct); // single use

            var claims = new[] {
                new Claim(ClaimTypes.Email, email),
                new Claim(TenantAuthClaimTypes.TenantId, tenantId.Value.ToString()),
            };
            var id = new ClaimsIdentity(claims, "HubCookie");
            await HttpContext.SignInAsync("HubCookie",
                new ClaimsPrincipal(id),
                new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) });

            return NoContent();
        }

        /// <summary>
        /// Clears the SignalR <c>hubauth</c> cookie. Safe to call without authentication.
        /// </summary>
        [HttpPost("auth/hub-signout")]
        [SwaggerResponse(204)]
        [AllowAnonymous]
        public async Task<IActionResult> SignOutHubCookie()
        {
            await HttpContext.SignOutAsync("HubCookie");
            return NoContent();
        }

        private static string BuildTicketKey(Guid tenantId, string ticket)
            => $"hub:ticket:{tenantId}:{ticket}";
    }
}
