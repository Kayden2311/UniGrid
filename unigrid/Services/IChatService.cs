using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using unigrid.Models;

namespace unigrid.Services;

public interface IChatService
{
    Task<ChatRoom?> GetRoomByWorkspaceIdAsync(Guid workspaceId);
    Task<List<ChatMessage>> GetRoomMessagesAsync(Guid roomId);
<<<<<<< HEAD
    Task<(ChatMessage? message, string? error)> SendChatMessageAsync(Guid workspaceId, Guid userId, string content, string activeChannel, Guid? selectedFileId);
=======
    Task<(ChatMessage? message, string? error)> SendChatMessageAsync(Guid workspaceId, Guid userId, string content, string activeChannel, Guid? selectedFileId, Guid? parentId = null);
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
    Task<string?> DeleteChatMessageAsync(Guid workspaceId, Guid userId, Guid messageId);
}
