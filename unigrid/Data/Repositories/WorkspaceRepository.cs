using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly UniGridDbContext _context;

    public WorkspaceRepository(UniGridDbContext context)
    {
        _context = context;
    }

    public async System.Threading.Tasks.Task<Workspace?> GetByJoinCodeAsync(string joinCode)
    {
        return await _context.Workspaces
            .Include(w => w.Owner)
            .FirstOrDefaultAsync(w => !w.IsDisabled && w.JoinCode == joinCode);
    }

    public async System.Threading.Tasks.Task<Workspace?> GetByIdAsync(Guid id)
    {
        return await _context.Workspaces
            .Include(w => w.Owner)
            .FirstOrDefaultAsync(w => !w.IsDisabled && w.Id == id);
    }

    public async System.Threading.Tasks.Task<List<Workspace>> GetUserWorkspacesAsync(Guid userId)
    {
        return await _context.Workspaces
            .Where(w => !w.IsDisabled && (w.OwnerId == userId || w.WorkspaceMembers.Any(m => m.UserId == userId)))
            .ToListAsync();
    }

    public async System.Threading.Tasks.Task AddAsync(Workspace workspace)
    {
        await _context.Workspaces.AddAsync(workspace);
    }

    public void Update(Workspace workspace)
    {
        _context.Workspaces.Update(workspace);
    }

    public void Remove(Workspace workspace)
    {
        workspace.IsDisabled = true;
        _context.Workspaces.Update(workspace);
    }
}
