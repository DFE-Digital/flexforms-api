using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Hubs;

/// <summary>
/// Tenant is read from the <c>hubauth</c> cookie claims, never from <c>HttpContext.RequestServices</c>:
/// under Azure SignalR Service the hub's HttpContext is synthesised (tenant middleware never runs on it),
/// and the request scope may already be disposed when hub lifecycle methods execute.
/// Group membership is removed automatically by SignalR when a connection closes.
/// </summary>
[Authorize(Policy = "Cookies.CanReadNotifications")]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userEmail = Context.User?.FindFirstValue(ClaimTypes.Email);
        var tenantClaim = Context.User?.FindFirstValue(TenantAuthClaimTypes.TenantId);

        if (!string.IsNullOrEmpty(userEmail) && Guid.TryParse(tenantClaim, out var tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}:user:{userEmail}");
        }
        else if (!string.IsNullOrEmpty(userEmail))
        {
            // Fallback for cases without tenant (shouldn't happen in production)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userEmail}");
        }

        await base.OnConnectedAsync();
    }
}
