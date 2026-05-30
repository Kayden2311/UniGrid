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
}
