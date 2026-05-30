using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public class FileRepository : IFileRepository
{
    private readonly UniGridDbContext _context;

    public FileRepository(UniGridDbContext context)
    {
        _context = context;
    }

    public async System.Threading.Tasks.Task<WorkspaceFile?> GetByIdAsync(Guid id)
    {
        return await _context.WorkspaceFiles
            .Include(wf => wf.User)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async System.Threading.Tasks.Task<List<WorkspaceFile>> GetWorkspaceFilesAsync(Guid workspaceId)
    {
        return await _context.WorkspaceFiles
            .Include(wf => wf.User)
            .Where(wf => wf.WorkspaceId == workspaceId)
            .OrderByDescending(wf => wf.CreatedAt)
            .ToListAsync();
    }

    public async System.Threading.Tasks.Task AddAsync(WorkspaceFile file)
    {
        await _context.WorkspaceFiles.AddAsync(file);
    }

    public void Remove(WorkspaceFile file)
    {
        _context.WorkspaceFiles.Remove(file);
    }

    public async System.Threading.Tasks.Task<long> GetUserStorageUsedAsync(Guid workspaceId, Guid userId)
    {
        return await _context.WorkspaceFiles
            .Where(f => f.WorkspaceId == workspaceId && f.UserId == userId)
            .SumAsync(f => f.FileSize);
    }

    public async System.Threading.Tasks.Task<long> GetWorkspaceStorageUsedAsync(Guid workspaceId)
    {
        return await _context.WorkspaceFiles
            .Where(f => f.WorkspaceId == workspaceId)
            .SumAsync(f => f.FileSize);
    }
}
