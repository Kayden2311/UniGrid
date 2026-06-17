using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using unigrid.Data.Repositories;
using unigrid.Hubs;
using unigrid.Models;

namespace unigrid.Services;

public class ChatService : IChatService
{
    private readonly IWorkspaceRepository _workspaceRepo;
    private readonly IMemberRepository _memberRepo;
    private readonly IChatRepository _chatRepo;
    private readonly IWorkspaceService _workspaceService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IWorkspaceRepository workspaceRepo,
        IMemberRepository memberRepo,
        IChatRepository chatRepo,
        IWorkspaceService workspaceService,
        IUnitOfWork unitOfWork,
        IMemoryCache cache,
        IHubContext<ChatHub> hubContext,
        ILogger<ChatService> logger)
    {
        _workspaceRepo = workspaceRepo;
        _memberRepo = memberRepo;
        _chatRepo = chatRepo;
        _workspaceService = workspaceService;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<ChatRoom?> GetRoomByWorkspaceIdAsync(Guid workspaceId)
    {
        return await _cache.GetOrCreateAsync($"WorkspaceChatRoom_{workspaceId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _chatRepo.GetRoomByWorkspaceIdAsync(workspaceId);
        });
    }

    public async Task<List<ChatMessage>> GetRoomMessagesAsync(Guid roomId)
    {
        return await _cache.GetOrCreateAsync($"WorkspaceChatMessages_{roomId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _chatRepo.GetRoomMessagesAsync(roomId);
        });
    }

    public async Task<(ChatMessage? message, string? error)> SendChatMessageAsync(Guid workspaceId, Guid userId, string content, string activeChannel, Guid? selectedFileId)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return (null, "Workspace not found.");

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        string userRole = userRecord?.Role ?? (workspace.OwnerId == userId ? "Manager" : "Member");

        if (userRole == "Viewer") return (null, "Viewer role cannot send chat messages.");
        if (string.IsNullOrEmpty(content)) return (null, "Message content cannot be empty.");

        var chatRoom = await GetRoomByWorkspaceIdAsync(workspaceId);
        if (chatRoom == null) return (null, "Chat room not found.");

        var user = await _memberRepo.GetUserByIdAsync(userId);
        if (user == null) return (null, "User profile not found.");

        // SYSTEM CHANNEL RULES COMMIT CHECK:
        if (content.StartsWith("[system:channel_rules]"))
        {
            try
            {
                string jsonStr = content.Substring("[system:channel_rules]".Length);
                var incomingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(jsonStr);
                if (incomingPayload != null && incomingPayload["allChannels"] != null)
                {
                    var incomingChannels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(incomingPayload["allChannels"].ToJsonString()) ?? new List<string>();
                    
                    var existingChannels = new List<string> { "general" };
                    string? currentSettings = workspace.SettingsJson;
                    if (!string.IsNullOrEmpty(currentSettings))
                    {
                        try
                        {
                            var existingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(currentSettings);
                            if (existingPayload != null && existingPayload["allChannels"] != null)
                            {
                                existingChannels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(existingPayload["allChannels"].ToJsonString()) ?? existingChannels;
                            }
                        }
                        catch {}
                    }

                    bool isAddingChannel = incomingChannels.Any(c => !existingChannels.Contains(c));
                    if (isAddingChannel)
                    {
                        if (!IsMemberAllowed(workspace, members, userId, "disabledCreateChannelUsers", userRole))
                        {
                            return (null, "You do not have permission to create chat channels.");
                        }
                    }
                    else
                    {
                        bool isManagerOrOwner = userRole == "Manager" || workspace.OwnerId == userId;
                        if (!isManagerOrOwner)
                        {
                            string ch = activeChannel ?? "general";
                            if (ch != "general")
                            {
                                var channelOwners = new Dictionary<string, string>();
                                var channelModerators = new Dictionary<string, List<string>>();
                                
                                if (!string.IsNullOrEmpty(currentSettings))
                                {
                                    try
                                    {
                                        var existingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(currentSettings);
                                        if (existingPayload != null)
                                        {
                                            if (existingPayload["channelOwners"] != null)
                                                channelOwners = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(existingPayload["channelOwners"].ToJsonString()) ?? channelOwners;
                                            if (existingPayload["channelModerators"] != null)
                                                channelModerators = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(existingPayload["channelModerators"].ToJsonString()) ?? channelModerators;
                                        }
                                    }
                                    catch {}
                                }

                                bool isChannelOwner = channelOwners.TryGetValue(ch, out var oId) && oId.ToLower() == userId.ToString().ToLower();
                                bool isChannelMod = channelModerators.TryGetValue(ch, out var mods) && mods.Any(m => m.ToLower() == userId.ToString().ToLower());
                                
                                if (!isChannelOwner && !isChannelMod)
                                {
                                    return (null, "You do not have permission to manage this channel's access rules.");
                                }

                                if (!isChannelOwner)
                                {
                                    // 1. Verify owner of the channel did not change
                                    string existingOwner = channelOwners.TryGetValue(ch, out var eo) ? eo.ToLower() : "";
                                    
                                    var incomingOwners = new Dictionary<string, string>();
                                    if (incomingPayload["channelOwners"] != null)
                                    {
                                        incomingOwners = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(incomingPayload["channelOwners"].ToJsonString()) ?? incomingOwners;
                                    }
                                    string incomingOwner = incomingOwners.TryGetValue(ch, out var io) ? io.ToLower() : "";
                                    
                                    if (existingOwner != incomingOwner)
                                    {
                                        return (null, "Only the channel owner or workspace owner/manager can transfer channel ownership.");
                                    }

                                    // 2. Verify their own Access did not change
                                    var existingLocked = new Dictionary<string, List<string>>();
                                    if (!string.IsNullOrEmpty(currentSettings))
                                    {
                                        try
                                        {
                                            var existingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(currentSettings);
                                            if (existingPayload != null && existingPayload["lockedChannels"] != null)
                                            {
                                                existingLocked = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(existingPayload["lockedChannels"].ToJsonString()) ?? existingLocked;
                                            }
                                        }
                                        catch {}
                                    }
                                    
                                    var incomingLocked = new Dictionary<string, List<string>>();
                                    if (incomingPayload["lockedChannels"] != null)
                                    {
                                        incomingLocked = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(incomingPayload["lockedChannels"].ToJsonString()) ?? incomingLocked;
                                    }

                                    var existingAccessList = existingLocked.TryGetValue(ch, out var elist) ? elist.Select(id => id.ToLower()).ToList() : new List<string>();
                                    var incomingAccessList = incomingLocked.TryGetValue(ch, out var ilist) ? ilist.Select(id => id.ToLower()).ToList() : new List<string>();

                                    string myId = userId.ToString().ToLower();
                                    bool hadAccess = existingAccessList.Contains(myId);
                                    bool hasAccessNow = incomingAccessList.Contains(myId);

                                    if (hadAccess != hasAccessNow)
                                    {
                                        return (null, "You cannot modify your own channel access.");
                                    }

                                    // 3. Verify their own Mod status did not change
                                    var incomingModsDict = new Dictionary<string, List<string>>();
                                    if (incomingPayload["channelModerators"] != null)
                                    {
                                        incomingModsDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(incomingPayload["channelModerators"].ToJsonString()) ?? incomingModsDict;
                                    }
                                    
                                    var existingModsList = channelModerators.TryGetValue(ch, out var emods) ? emods.Select(id => id.ToLower()).ToList() : new List<string>();
                                    var incomingModsList = incomingModsDict.TryGetValue(ch, out var imods) ? imods.Select(id => id.ToLower()).ToList() : new List<string>();

                                    bool wasMod = existingModsList.Contains(myId);
                                    bool isModNow = incomingModsList.Contains(myId);

                                    if (wasMod != isModNow)
                                    {
                                        return (null, "You cannot modify your own channel moderator status.");
                                    }

                                    // 4. Verify they did not edit moderators for other users
                                    var otherExistingMods = existingModsList.Where(id => id != myId).OrderBy(id => id).ToList();
                                    var otherIncomingMods = incomingModsList.Where(id => id != myId).OrderBy(id => id).ToList();
                                    if (!otherExistingMods.SequenceEqual(otherIncomingMods))
                                    {
                                        return (null, "Only the channel owner or workspace owner/manager can modify moderator roles.");
                                    }
                                }
                            }
                        }
                    }
                }

                workspace.SettingsJson = jsonStr;
                _workspaceRepo.Update(workspace);
                await _unitOfWork.SaveChangesAsync();

                _cache.Remove($"Workspace_{workspace.JoinCode}");

                var broadcastPayload = new
                {
                    id = Guid.NewGuid(),
                    roomId = chatRoom.Id,
                    senderId = userId,
                    senderName = user.FullName,
                    content = "[system:channel_rules]",
                    rawContent = content,
                    sentAt = DateTime.UtcNow,
                    channel = "general"
                };
                await _hubContext.Clients.Group(workspaceId.ToString()).SendAsync("ReceiveChatMessage", broadcastPayload);

                return (null, null); // Return rules updated successfully
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse rules payload.");
                return (null, "Invalid channel rules payload.");
            }
        }

        // Regular message sending
        string cleanContent = Helpers.InputSanitizer.SanitizeInput(content);
        string contentWithChannel = cleanContent;
        if (!string.IsNullOrEmpty(activeChannel) && activeChannel != "general")
        {
            contentWithChannel = $"[channel:{activeChannel}]{cleanContent}";
        }

        if (selectedFileId.HasValue)
        {
            contentWithChannel = $"[file:{selectedFileId.Value}]{contentWithChannel}";
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            RoomId = chatRoom.Id,
            SenderId = userId,
            Content = contentWithChannel,
            SentAt = DateTime.UtcNow,
            IsDeleted = false
        };

        await _chatRepo.AddMessageAsync(message);
        await _unitOfWork.SaveChangesAsync();

        _cache.Remove($"WorkspaceChatMessages_{chatRoom.Id}");

        var payload = new
        {
            id = message.Id,
            roomId = message.RoomId,
            senderId = message.SenderId,
            senderName = user.FullName,
            content = cleanContent,
            rawContent = message.Content,
            sentAt = message.SentAt,
            channel = string.IsNullOrEmpty(activeChannel) ? "general" : activeChannel
        };

        await _hubContext.Clients.Group(workspaceId.ToString()).SendAsync("ReceiveChatMessage", payload);

        return (message, null);
    }

    public async Task<string?> DeleteChatMessageAsync(Guid workspaceId, Guid userId, Guid messageId)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        string userRole = userRecord?.Role ?? (workspace.OwnerId == userId ? "Manager" : "Member");

        var chatRoom = await GetRoomByWorkspaceIdAsync(workspaceId);
        if (chatRoom == null) return "Chat room not found.";

        var message = await _chatRepo.GetMessageByIdAsync(messageId);
        if (message == null || message.RoomId != chatRoom.Id) return "Message not found.";

        bool isAuthorized = message.SenderId == userId;

        if (!isAuthorized)
        {
            return "You do not have permission to revoke this message.";
        }



        message.IsDeleted = true;
        message.Content = "[deleted_message]" + message.Content;
        _chatRepo.UpdateMessage(message);
        await _unitOfWork.SaveChangesAsync();

        _cache.Remove($"WorkspaceChatMessages_{chatRoom.Id}");

        var payload = new { messageId = messageId };
        await _hubContext.Clients.Group(workspaceId.ToString()).SendAsync("ReceiveMessageDeletion", payload);

        _logger.LogInformation("Chat message revoked: {MessageId} in Workspace {WorkspaceId} by User {UserId}", messageId, workspaceId, userId);

        return null; // Success
    }

    private bool IsMemberAllowed(Workspace workspace, List<WorkspaceMember> members, Guid memberId, string key, string role)
    {
        if (workspace == null) return true;
        if (workspace.OwnerId == memberId) return true;

        var memberRecord = members.FirstOrDefault(m => m.UserId == memberId);
        if (memberRecord != null && memberRecord.Role == "Manager")
        {
            return true;
        }

        bool defaultAllowed = (role != "Viewer");

        if (!string.IsNullOrEmpty(workspace.SettingsJson))
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(workspace.SettingsJson);
                if (payload != null && payload[key] != null)
                {
                    var disabledList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(payload[key].ToJsonString());
                    if (disabledList != null)
                    {
                        return !disabledList.Contains(memberId.ToString().ToLower());
                    }
                }
            }
            catch {}
        }

        return defaultAllowed;
    }
}
