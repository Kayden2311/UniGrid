using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using unigrid.Models;

namespace unigrid.Services;

public interface ITaskService
{
    Task<string?> CreateTaskAsync(Guid workspaceId, Guid creatorId, string title, string description, int priority, Guid? assigneeId, DateTime? dueDate, int status, Guid? categoryId = null, bool isCounterTask = false, int targetCount = 1);
    Task<string?> UpdateTaskStatusAsync(Guid? workspaceId, Guid userId, Guid taskId, int status);
    Task<string?> EditTaskAsync(Guid workspaceId, Guid userId, Guid editTaskId, string editTaskTitle, string editTaskDescription, int editTaskPriority, Guid? editTaskAssigneeId, DateTime? editTaskDueDate, Guid? editCategoryId = null, bool editIsCounterTask = false, int editTargetCount = 1);
    Task<string?> DeleteTaskAsync(Guid workspaceId, Guid userId, Guid taskId);
<<<<<<< HEAD
    Task<string?> AddTaskCommentAsync(Guid workspaceId, Guid userId, Guid taskId, string content);
=======
    Task<string?> AddTaskCommentAsync(Guid workspaceId, Guid userId, Guid taskId, string content, Guid? parentId = null);
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49

    // Counter task counter update
    Task<string?> UpdateTaskCounterAsync(Guid? workspaceId, Guid userId, Guid taskId, int currentCount);

    // TaskCategory methods
    Task<List<TaskCategory>> GetWorkspaceCategoriesAsync(Guid workspaceId);
    Task<string?> CreateCategoryAsync(Guid workspaceId, Guid userId, string name, string? description, string colorHex);
    Task<string?> UpdateCategoryAsync(Guid workspaceId, Guid userId, Guid categoryId, string name, string? description, string colorHex);
    Task<string?> DeleteCategoryAsync(Guid workspaceId, Guid userId, Guid categoryId);

    // KpiTarget methods
    Task<List<KpiTarget>> GetWorkspaceTargetsAsync(Guid workspaceId);
    Task<string?> CreateKpiTargetAsync(Guid workspaceId, Guid creatorId, Guid userId, Guid categoryId, string periodType, DateTime startDate, DateTime endDate, int targetValue);
    Task<string?> DeleteKpiTargetAsync(Guid workspaceId, Guid userId, Guid targetId);
    Task<KpiReportDto> GetKpiReportAsync(Guid workspaceId, string periodType, DateTime targetDate);
}
