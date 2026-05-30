using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public class MemberRepository : IMemberRepository
{
    private readonly UniGridDbContext _context;

    public MemberRepository(UniGridDbContext context)
    {
        _context = context;
    }

    public async System.Threading.Tasks.Task<List<WorkspaceMember>> GetWorkspaceMembersAsync(Guid workspaceId)
    {
        return await _context.WorkspaceMembers
            .Include(wm => wm.User)
            .Where(wm => wm.WorkspaceId == workspaceId)
            .ToListAsync();
    }

    public async System.Threading.Tasks.Task<WorkspaceMember?> GetMemberAsync(Guid workspaceId, Guid userId)
    {
        return await _context.WorkspaceMembers
            .Include(wm => wm.User)
            .FirstOrDefaultAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == userId);
    }

    public async System.Threading.Tasks.Task AddMemberAsync(WorkspaceMember member)
    {
        await _context.WorkspaceMembers.AddAsync(member);
    }

    public void UpdateMember(WorkspaceMember member)
    {
        _context.WorkspaceMembers.Update(member);
    }

    public void RemoveMember(WorkspaceMember member)
    {
        _context.WorkspaceMembers.Remove(member);
    }

    public async System.Threading.Tasks.Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users
            .Include(u => u.Account)
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async System.Threading.Tasks.Task<User?> GetUserByAccountIdAsync(Guid accountId)
    {
        return await _context.Users
            .Include(u => u.Account)
            .FirstOrDefaultAsync(u => u.AccountId == accountId);
    }

    public async System.Threading.Tasks.Task<User?> GetUserByEmailAsync(string email)
    {
        return await _context.Users
            .Include(u => u.Account)
            .FirstOrDefaultAsync(u => u.Account.Email.ToLower() == email.ToLower());
    }

    public async System.Threading.Tasks.Task<WorkspaceInvitation?> GetPendingInvitationAsync(Guid workspaceId, string email)
    {
        return await _context.WorkspaceInvitations
            .FirstOrDefaultAsync(i => i.WorkspaceId == workspaceId && i.InviteeEmail.ToLower() == email.ToLower() && i.Status == "Pending");
    }

    public async System.Threading.Tasks.Task<int> GetPendingInvitationsCountAsync(Guid workspaceId)
    {
        return await _context.WorkspaceInvitations
            .CountAsync(i => i.WorkspaceId == workspaceId && i.Status == "Pending");
    }

    public async System.Threading.Tasks.Task AddInvitationAsync(WorkspaceInvitation invitation)
    {
        await _context.WorkspaceInvitations.AddAsync(invitation);
    }
}
