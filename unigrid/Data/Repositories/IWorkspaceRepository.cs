using System;
using System.Collections.Generic;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public interface IWorkspaceRepository
{
    System.Threading.Tasks.Task<Workspace?> GetByJoinCodeAsync(string joinCode);
    System.Threading.Tasks.Task<Workspace?> GetByIdAsync(Guid id);
    System.Threading.Tasks.Task<List<Workspace>> GetUserWorkspacesAsync(Guid userId);
    System.Threading.Tasks.Task AddAsync(Workspace workspace);
    void Update(Workspace workspace);
    void Remove(Workspace workspace);
}
