using System;
using System.Collections.Generic;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public interface ITaskRepository
{
    System.Threading.Tasks.Task<unigrid.Models.Task?> GetByIdAsync(Guid id);
    System.Threading.Tasks.Task<List<unigrid.Models.Task>> GetWorkspaceTasksAsync(Guid workspaceId);
    System.Threading.Tasks.Task AddAsync(unigrid.Models.Task task);
    void Update(unigrid.Models.Task task);
    void Remove(unigrid.Models.Task task);
    System.Threading.Tasks.Task<List<PersonalSchedule>> GetAssociatedSchedulesAsync(Guid taskId);
    void RemoveSchedules(IEnumerable<PersonalSchedule> schedules);
    System.Threading.Tasks.Task AddNotificationAsync(Notification notification);
    System.Threading.Tasks.Task AddCommentAsync(TaskComment comment);

    // TaskCategory methods
    System.Threading.Tasks.Task<List<TaskCategory>> GetWorkspaceCategoriesAsync(Guid workspaceId);
    System.Threading.Tasks.Task<TaskCategory?> GetCategoryByIdAsync(Guid id);
    System.Threading.Tasks.Task AddCategoryAsync(TaskCategory category);
    void RemoveCategory(TaskCategory category);

    // KpiTarget methods
    System.Threading.Tasks.Task<List<KpiTarget>> GetWorkspaceTargetsAsync(Guid workspaceId);
    System.Threading.Tasks.Task<KpiTarget?> GetTargetByIdAsync(Guid id);
    System.Threading.Tasks.Task AddTargetAsync(KpiTarget target);
    void RemoveTarget(KpiTarget target);
    System.Threading.Tasks.Task<List<KpiTarget>> GetUserTargetsForPeriodAsync(Guid workspaceId, Guid userId, string periodType, DateTime startDate, DateTime endDate);
}
