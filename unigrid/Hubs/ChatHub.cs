using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace unigrid.Hubs;

public class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var accountIdClaim = httpContext?.User.FindFirst("AccountId")?.Value;
        if (!string.IsNullOrEmpty(accountIdClaim))
        {
            // Join a group for their AccountId dynamically
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Account_{accountIdClaim}");
        }
        await base.OnConnectedAsync();
    }

    public async Task JoinWorkspace(string workspaceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, workspaceId);
    }

    public async Task SendMessage(string workspaceId, string user, string message)
    {
        await Clients.Group(workspaceId).SendAsync("ReceiveMessage", user, message);
    }
}
