using System;
using System.Collections.Generic;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public interface IChatRepository
{
    System.Threading.Tasks.Task<ChatRoom?> GetRoomByWorkspaceIdAsync(Guid workspaceId);
    System.Threading.Tasks.Task<List<ChatMessage>> GetRoomMessagesAsync(Guid roomId);
    System.Threading.Tasks.Task<ChatMessage?> GetMessageByIdAsync(Guid messageId);
    System.Threading.Tasks.Task AddMessageAsync(ChatMessage message);
    void UpdateMessage(ChatMessage message);
}
