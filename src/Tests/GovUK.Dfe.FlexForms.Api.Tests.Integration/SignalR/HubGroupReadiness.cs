using GovUK.Dfe.FlexForms.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.SignalR;

/// <summary>
/// <c>HubConnection.StartAsync</c> returns once the handshake is done, which can be before
/// <see cref="NotificationHub.OnConnectedAsync"/> has added the connection to its group, so a message sent
/// straight after can be dropped. Tests wait here first: probes are sent to the group until the connection
/// receives one, and only then is the message under test sent.
/// </summary>
internal static class HubGroupReadiness
{
    private const string ProbeMethod = "test.group-ready";

    public static async Task WaitUntilInGroupAsync(
        HubConnection connection,
        IHubContext<NotificationHub> hub,
        string group,
        TimeSpan? timeout = null)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var probeHandler = connection.On<string>(ProbeMethod, _ => ready.TrySetResult());
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (!ready.Task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"The connection was not in group '{group}' before the timeout.");

            await hub.Clients.Group(group).SendAsync(ProbeMethod, "probe");
            await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromMilliseconds(100)));
        }
    }
}
