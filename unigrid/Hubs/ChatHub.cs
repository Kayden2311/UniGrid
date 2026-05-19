using Microsoft.AspNetCore.SignalR;

namespace unigrid.Hubs;

public class ChatHub : Hub
{
    public async Task JoinWorkspace(string workspaceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, workspaceId);
    }

    public async Task SendMessage(string workspaceId, string user, string message)
    {
        await Clients.Group(workspaceId).SendAsync("ReceiveMessage", user, message);
    }
}
