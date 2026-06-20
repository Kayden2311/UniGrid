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

public class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceRepository _workspaceRepo;
    private readonly IMemberRepository _memberRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly IFileRepository _fileRepo;
    private readonly IChatRepository _chatRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        IWorkspaceRepository workspaceRepo,
        IMemberRepository memberRepo,
        ITaskRepository taskRepo,
        IFileRepository fileRepo,
        IChatRepository chatRepo,
        IUnitOfWork unitOfWork,
        IMemoryCache cache,
        IHubContext<ChatHub> hubContext,
        ILogger<WorkspaceService> logger)
    {
        _workspaceRepo = workspaceRepo;
        _memberRepo = memberRepo;
        _taskRepo = taskRepo;
        _fileRepo = fileRepo;
        _chatRepo = chatRepo;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<Workspace?> GetWorkspaceByJoinCodeAsync(string joinCode)
    {
        return await _cache.GetOrCreateAsync($"Workspace_{joinCode}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _workspaceRepo.GetByJoinCodeAsync(joinCode);
        });
    }

    public async Task<List<WorkspaceMember>> GetWorkspaceMembersAsync(Guid workspaceId)
    {
        return await _cache.GetOrCreateAsync($"WorkspaceMembers_{workspaceId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        });
    }

    public async Task<WorkspaceMember?> GetMemberAsync(Guid workspaceId, Guid userId)
    {
        var members = await GetWorkspaceMembersAsync(workspaceId);
        return members.FirstOrDefault(m => m.UserId == userId);
    }

    public async Task<List<Workspace>> GetUserWorkspacesCachedAsync(Guid userId)
    {
        return await _cache.GetOrCreateAsync($"UserWorkspaces_{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _workspaceRepo.GetUserWorkspacesAsync(userId);
        });
    }

    public async Task<string?> LeaveWorkspaceAsync(Guid workspaceId, Guid userId)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var currentMember = members.FirstOrDefault(m => m.UserId == userId);
        if (currentMember == null) return "You are not a member of this workspace.";

        bool isWorkspaceOwner = workspace.OwnerId == userId;
        bool isManager = currentMember.Role == "Manager" || isWorkspaceOwner;

        var otherMembers = members.Where(m => m.UserId != userId).ToList();

        if (isManager && otherMembers.Any())
        {
            // succession logic:
            var successor = otherMembers.FirstOrDefault(m => m.Role == "Vice Manager") ?? otherMembers.FirstOrDefault();
            if (successor != null)
            {
                successor.Role = "Manager";
                _memberRepo.UpdateMember(successor);

                if (isWorkspaceOwner)
                {
                    workspace.OwnerId = successor.UserId;
                    _workspaceRepo.Update(workspace);
                }

                _logger.LogInformation("Workspace Succession: User {UserId} is leaving. Promoted user {SuccessorId} to Manager.", userId, successor.UserId);
            }
        }

        _memberRepo.RemoveMember(currentMember);
        await _unitOfWork.SaveChangesAsync();

        EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        _cache.Remove($"UserWorkspaces_{userId}");

        return null; // Success
    }

    public async Task<string?> InviteMemberAsync(Guid workspaceId, Guid inviterId, string email, string role, string displayRole)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var inviterRecord = members.FirstOrDefault(m => m.UserId == inviterId);
        string inviterRole = inviterRecord?.Role ?? (workspace.OwnerId == inviterId ? "Manager" : "Member");

        if (workspace.OwnerId != inviterId && inviterRecord == null)
        {
            return "Access denied to this workspace.";
        }

        if (inviterRole != "Manager" && inviterRole != "Vice Manager")
        {
            return "Only Managers or Vice Managers have permission to invite members.";
        }

        if (workspace.PackageTier == "Personal") return "Cannot invite members in a Personal plan workspace.";
        if (string.IsNullOrEmpty(email)) return "Email is required.";

        var emailTrimmed = email.Trim().ToLower();

        // 1. Check if user is already a member
        var inviteeUser = await _memberRepo.GetUserByEmailAsync(emailTrimmed);
        if (inviteeUser != null)
        {
            var alreadyMember = (await _memberRepo.GetWorkspaceMembersAsync(workspaceId)).Any(m => m.UserId == inviteeUser.Id);
            if (alreadyMember) return "This user is already a member of this workspace.";
        }

        // 2. Check pending invites
        var existingInvite = await _memberRepo.GetPendingInvitationAsync(workspaceId, emailTrimmed);
        if (existingInvite != null) return "An invitation has already been sent to this email and is pending.";

        // 3. Enforce pricing limits
        int currentMembersCount = (await _memberRepo.GetWorkspaceMembersAsync(workspaceId)).Count;
        int pendingInvitesCount = await _memberRepo.GetPendingInvitationsCountAsync(workspaceId);
        
        int maxMembersAllowed = 5;
        string tier = workspace.PackageTier ?? "Free";
        if (tier == "Pro") maxMembersAllowed = 10;
        else if (tier == "ProPlus") maxMembersAllowed = 15;
        else if (tier == "Business") maxMembersAllowed = 30;

        if (currentMembersCount + pendingInvitesCount >= maxMembersAllowed)
        {
            return $"Cannot send invitation. The workspace has reached the member limit ({maxMembersAllowed}) of the {tier} plan.";
        }

        var invitation = new WorkspaceInvitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            InviterId = inviterId,
            InviteeEmail = emailTrimmed,
            Role = role,
            DisplayRole = Helpers.InputSanitizer.SanitizeInput(displayRole),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        await _memberRepo.AddInvitationAsync(invitation);

        if (inviteeUser != null)
        {
            var inviter = await _memberRepo.GetUserByIdAsync(inviterId);
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = inviteeUser.Id,
                Message = $"{inviter?.FullName ?? "Someone"} has invited you to join Workspace '{workspace.Name}' as a '{role}'.",
                Type = "WorkspaceInvitation",
                Link = $"/api/invitations/{invitation.Id}/accept",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                RelatedId = workspaceId
            };
            await _taskRepo.AddNotificationAsync(notification);
        }

        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation("Invitation sent to email {Email} as {Role} in Workspace {WorkspaceId}", emailTrimmed, role, workspaceId);

        return null; // Success
    }

    public async Task<string?> UpdateMemberRoleAsync(Guid workspaceId, Guid managerId, Guid memberId, string newRole, string newDisplayRole, bool canDeleteFile, bool canCreateTask, bool canEditTask, bool canCreateChannel, bool canDeleteTask)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var managerRecord = members.FirstOrDefault(m => m.UserId == managerId);
        string managerRole = managerRecord?.Role ?? (workspace.OwnerId == managerId ? "Manager" : "Member");

        var memberToUpdate = await _memberRepo.GetMemberAsync(workspaceId, memberId);
        if (memberToUpdate == null) return "Member does not exist in this workspace.";

        // Self-updating display role is allowed for anyone
        if (memberId == managerId)
        {
            memberToUpdate.DisplayRole = Helpers.InputSanitizer.SanitizeInput(newDisplayRole);
            _memberRepo.UpdateMember(memberToUpdate);
            await _unitOfWork.SaveChangesAsync();

            // Clear cache
            _cache.Remove($"Workspace_{workspace.JoinCode}");
            _cache.Remove($"WorkspaceMembers_{workspaceId}");
            EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());

            return null; // Success
        }

        // Only manager can update roles for others
        if (managerRole != "Manager") return "You do not have permission to update roles. Only Managers can update roles.";
        if (newRole == "Manager") return "A workspace is only allowed to have a single Manager (the Owner). You can appoint this member as a Vice Manager instead.";

        var validRoles = new List<string> { "Vice Manager", "Member", "Viewer" };
        if (!validRoles.Contains(newRole)) return "Invalid role specified.";

        memberToUpdate.Role = newRole;
        memberToUpdate.DisplayRole = Helpers.InputSanitizer.SanitizeInput(newDisplayRole);
        memberToUpdate.CanDeleteFile = canDeleteFile;
        memberToUpdate.CanCreateTask = canCreateTask;
        memberToUpdate.CanEditTask = canEditTask;
        _memberRepo.UpdateMember(memberToUpdate);

        // SCHEMA-LESS VIRTUAL PERMISSIONS MANAGEMENT IN settingsJson
        var disabledCreateChannel = new List<string>();
        var disabledCreateTask = new List<string>();
        var disabledEditTask = new List<string>();
        var disabledDeleteFile = new List<string>();
        var disabledDeleteTask = new List<string>();

        var lockedChannels = new Dictionary<string, List<string>>();
        var channelOwners = new Dictionary<string, string>();
        var channelModerators = new Dictionary<string, List<string>>();
        var allChannels = new List<string> { "general" };

        string? jsonStr = workspace.SettingsJson;
        if (!string.IsNullOrEmpty(jsonStr))
        {
            try
            {
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonStr);
                if (jsonNode != null)
                {
                    if (jsonNode["lockedChannels"] != null)
                        lockedChannels = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(jsonNode["lockedChannels"].ToJsonString()) ?? lockedChannels;
                    if (jsonNode["channelOwners"] != null)
                        channelOwners = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonNode["channelOwners"].ToJsonString()) ?? channelOwners;
                    if (jsonNode["channelModerators"] != null)
                        channelModerators = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(jsonNode["channelModerators"].ToJsonString()) ?? channelModerators;
                    if (jsonNode["allChannels"] != null)
                        allChannels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(jsonNode["allChannels"].ToJsonString()) ?? allChannels;

                    disabledCreateChannel = ParseListFromJsonNode(jsonNode["disabledCreateChannelUsers"]);
                    disabledCreateTask = ParseListFromJsonNode(jsonNode["disabledCreateTaskUsers"]);
                    disabledEditTask = ParseListFromJsonNode(jsonNode["disabledEditTaskUsers"]);
                    disabledDeleteFile = ParseListFromJsonNode(jsonNode["disabledDeleteFileUsers"]);
                    disabledDeleteTask = ParseListFromJsonNode(jsonNode["disabledDeleteTaskUsers"]);
                }
            }
            catch {}
        }

        string targetUserGuidStr = memberId.ToString().ToLower();
        UpdateDisabledListHelper(disabledCreateChannel, targetUserGuidStr, canCreateChannel);
        UpdateDisabledListHelper(disabledCreateTask, targetUserGuidStr, canCreateTask);
        UpdateDisabledListHelper(disabledEditTask, targetUserGuidStr, canEditTask);
        UpdateDisabledListHelper(disabledDeleteFile, targetUserGuidStr, canDeleteFile);
        UpdateDisabledListHelper(disabledDeleteTask, targetUserGuidStr, canDeleteTask);

        var newPayload = new
        {
            lockedChannels = lockedChannels,
            channelOwners = channelOwners,
            channelModerators = channelModerators,
            allChannels = allChannels,
            disabledCreateChannelUsers = disabledCreateChannel,
            disabledCreateTaskUsers = disabledCreateTask,
            disabledEditTaskUsers = disabledEditTask,
            disabledDeleteFileUsers = disabledDeleteFile,
            disabledDeleteTaskUsers = disabledDeleteTask
        };

        var serializedPayload = System.Text.Json.JsonSerializer.Serialize(newPayload);
        workspace.SettingsJson = serializedPayload;
        _workspaceRepo.Update(workspace);
        await _unitOfWork.SaveChangesAsync();

        // Broadcast real-time SignalR rules update payload to all active clients
        var chatRoom = await _chatRepo.GetRoomByWorkspaceIdAsync(workspaceId);
        if (chatRoom != null)
        {
            var managerUser = await _memberRepo.GetUserByIdAsync(managerId);
            var broadcastPayload = new
            {
                id = Guid.NewGuid(),
                roomId = chatRoom.Id,
                senderId = managerId,
                senderName = managerUser?.FullName ?? "Manager",
                content = "[system:channel_rules]",
                rawContent = "[system:channel_rules]" + serializedPayload,
                sentAt = DateTime.UtcNow,
                channel = "general"
            };
            await _hubContext.Clients.Group(workspaceId.ToString()).SendAsync("ReceiveChatMessage", broadcastPayload);
        }

        // Evict caches
        _cache.Remove($"Workspace_{workspace.JoinCode}");
        _cache.Remove($"WorkspaceMembers_{workspaceId}");
        EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());

        return null; // Success
    }

    public async Task<string?> TransferOwnershipAsync(Guid workspaceId, Guid currentOwnerId, Guid newOwnerId)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        if (workspace.OwnerId != currentOwnerId) return "Only the workspace owner can transfer ownership.";
        if (currentOwnerId == newOwnerId) return "You are already the workspace owner.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var targetMember = members.FirstOrDefault(m => m.UserId == newOwnerId);
        if (targetMember == null) return "Selected member does not exist in this workspace.";

        var currentOwnerMember = members.FirstOrDefault(m => m.UserId == currentOwnerId);
        if (currentOwnerMember != null)
        {
            currentOwnerMember.Role = "Vice Manager";
            _memberRepo.UpdateMember(currentOwnerMember);
        }

        targetMember.Role = "Manager";
        targetMember.CanCreateTask = true;
        targetMember.CanEditTask = true;
        targetMember.CanDeleteFile = true;
        _memberRepo.UpdateMember(targetMember);

        workspace.OwnerId = newOwnerId;
        _workspaceRepo.Update(workspace);

        await _unitOfWork.SaveChangesAsync();

        // Evict caches
        _cache.Remove($"Workspace_{workspace.JoinCode}");
        _cache.Remove($"WorkspaceMembers_{workspaceId}");
        EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        _cache.Remove($"UserWorkspaces_{currentOwnerId}");
        _cache.Remove($"UserWorkspaces_{newOwnerId}");

        return null; // Success
    }

    public void EvictWorkspaceCache(Guid workspaceId, List<Guid> memberIds)
    {
        _cache.Remove($"WorkspaceTasks_{workspaceId}");
        _cache.Remove($"WorkspaceFiles_{workspaceId}");
        _cache.Remove($"WorkspaceChatRoom_{workspaceId}");

        if (memberIds != null)
        {
            foreach (var userId in memberIds)
            {
                _cache.Remove($"UserWorkspaces_{userId}");
                _cache.Remove($"UserTasks_{userId}");
            }
        }
    }

    private List<string> ParseListFromJsonNode(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node == null) return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(node.ToJsonString()) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private void UpdateDisabledListHelper(List<string> list, string userGuidStr, bool allowed)
    {
        if (!allowed)
        {
            if (!list.Contains(userGuidStr)) list.Add(userGuidStr);
        }
        else
        {
            list.Remove(userGuidStr);
        }
    }
}
