using System;
using System.Collections.Generic;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public interface IFileRepository
{
    System.Threading.Tasks.Task<WorkspaceFile?> GetByIdAsync(Guid id);
    System.Threading.Tasks.Task<List<WorkspaceFile>> GetWorkspaceFilesAsync(Guid workspaceId);
    System.Threading.Tasks.Task AddAsync(WorkspaceFile file);
    void Remove(WorkspaceFile file);
    System.Threading.Tasks.Task<long> GetUserStorageUsedAsync(Guid workspaceId, Guid userId);
    System.Threading.Tasks.Task<long> GetWorkspaceStorageUsedAsync(Guid workspaceId);
}
