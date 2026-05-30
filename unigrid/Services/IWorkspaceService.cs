using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using unigrid.Models;

namespace unigrid.Services;

public interface IWorkspaceService
{
    Task<Workspace?> GetWorkspaceByJoinCodeAsync(string joinCode);
    Task<List<WorkspaceMember>> GetWorkspaceMembersAsync(Guid workspaceId);
    Task<WorkspaceMember?> GetMemberAsync(Guid workspaceId, Guid userId);
    Task<List<Workspace>> GetUserWorkspacesCachedAsync(Guid userId);
    
    // Actions
    Task<string?> LeaveWorkspaceAsync(Guid workspaceId, Guid userId);
    Task<string?> InviteMemberAsync(Guid workspaceId, Guid inviterId, string email, string role, string displayRole);
    Task<string?> UpdateMemberRoleAsync(Guid workspaceId, Guid managerId, Guid memberId, string newRole, string newDisplayRole, bool canDeleteFile, bool canCreateTask, bool canEditTask, bool canCreateChannel, bool canDeleteTask);
    Task<string?> TransferOwnershipAsync(Guid workspaceId, Guid currentOwnerId, Guid newOwnerId);
    
    // Cache Helpers
    void EvictWorkspaceCache(Guid workspaceId, List<Guid> memberIds);
}
