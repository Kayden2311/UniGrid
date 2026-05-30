using System;
using System.Threading.Tasks;
using unigrid.Models;

namespace unigrid.Services;

public interface ITaskService
{
    Task<string?> CreateTaskAsync(Guid workspaceId, Guid creatorId, string title, string description, int priority, Guid? assigneeId, DateTime? dueDate, int status);
    Task<string?> UpdateTaskStatusAsync(Guid workspaceId, Guid userId, Guid taskId, int status);
    Task<string?> EditTaskAsync(Guid workspaceId, Guid userId, Guid editTaskId, string editTaskTitle, string editTaskDescription, int editTaskPriority, Guid? editTaskAssigneeId, DateTime? editTaskDueDate);
    Task<string?> DeleteTaskAsync(Guid workspaceId, Guid userId, Guid taskId);
    Task<string?> AddTaskCommentAsync(Guid workspaceId, Guid userId, Guid taskId, string content);
}
