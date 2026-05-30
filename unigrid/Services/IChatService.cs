using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using unigrid.Models;

namespace unigrid.Services;

public interface IChatService
{
    Task<ChatRoom?> GetRoomByWorkspaceIdAsync(Guid workspaceId);
    Task<List<ChatMessage>> GetRoomMessagesAsync(Guid roomId);
    Task<(ChatMessage? message, string? error)> SendChatMessageAsync(Guid workspaceId, Guid userId, string content, string activeChannel, Guid? selectedFileId);
    Task<string?> DeleteChatMessageAsync(Guid workspaceId, Guid userId, Guid messageId);
}
