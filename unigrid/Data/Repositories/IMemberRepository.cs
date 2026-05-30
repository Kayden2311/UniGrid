using System;
using System.Collections.Generic;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public interface IMemberRepository
{
    System.Threading.Tasks.Task<List<WorkspaceMember>> GetWorkspaceMembersAsync(Guid workspaceId);
    System.Threading.Tasks.Task<WorkspaceMember?> GetMemberAsync(Guid workspaceId, Guid userId);
    System.Threading.Tasks.Task AddMemberAsync(WorkspaceMember member);
    void UpdateMember(WorkspaceMember member);
    void RemoveMember(WorkspaceMember member);
    
    // Invitations and Profiles
    System.Threading.Tasks.Task<User?> GetUserByIdAsync(Guid userId);
    System.Threading.Tasks.Task<User?> GetUserByAccountIdAsync(Guid accountId);
    System.Threading.Tasks.Task<User?> GetUserByEmailAsync(string email);
    System.Threading.Tasks.Task<WorkspaceInvitation?> GetPendingInvitationAsync(Guid workspaceId, string email);
    System.Threading.Tasks.Task<int> GetPendingInvitationsCountAsync(Guid workspaceId);
    System.Threading.Tasks.Task AddInvitationAsync(WorkspaceInvitation invitation);
}
