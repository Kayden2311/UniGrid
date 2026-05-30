using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using unigrid.Data.Repositories;
using unigrid.Hubs;
using unigrid.Models;

namespace unigrid.Services;

public class TaskService : ITaskService
{
    private readonly IWorkspaceRepository _workspaceRepo;
    private readonly IMemberRepository _memberRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly IWorkspaceService _workspaceService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<TaskService> _logger;

    public TaskService(
        IWorkspaceRepository workspaceRepo,
        IMemberRepository memberRepo,
        ITaskRepository taskRepo,
        IWorkspaceService workspaceService,
        IUnitOfWork unitOfWork,
        IMemoryCache cache,
        IHubContext<ChatHub> hubContext,
        ILogger<TaskService> logger)
    {
        _workspaceRepo = workspaceRepo;
        _memberRepo = memberRepo;
        _taskRepo = taskRepo;
        _workspaceService = workspaceService;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<string?> CreateTaskAsync(Guid workspaceId, Guid creatorId, string title, string description, int priority, Guid? assigneeId, DateTime? dueDate, int status)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var creatorRecord = members.FirstOrDefault(m => m.UserId == creatorId);
        string creatorRole = creatorRecord?.Role ?? (workspace.OwnerId == creatorId ? "Manager" : "Member");

        bool canCreate = IsMemberAllowed(workspace, members, creatorId, "disabledCreateTaskUsers", creatorRole);
        if (!canCreate) return "You do not have permission to create tasks.";

        if (string.IsNullOrEmpty(title)) return "Task title is required.";

        string sanitizedTitle = Helpers.InputSanitizer.SanitizeInput(title);
        string sanitizedDescription = Helpers.InputSanitizer.SanitizeInput(description);

        DateTime? finalDueDate = dueDate;
        if (finalDueDate.HasValue && finalDueDate.Value.Hour == 0 && finalDueDate.Value.Minute == 0 && finalDueDate.Value.Second == 0)
        {
            finalDueDate = finalDueDate.Value.Date.AddHours(23).AddMinutes(50);
        }

        var task = new unigrid.Models.Task
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            AssigneeId = assigneeId,
            Title = sanitizedTitle,
            Description = sanitizedDescription,
            Status = status,
            Priority = priority,
            DueDate = finalDueDate,
            CreatedAt = DateTime.UtcNow
        };

        await _taskRepo.AddAsync(task);

        var creator = await _memberRepo.GetUserByIdAsync(creatorId);

        if (task.AssigneeId.HasValue && task.AssigneeId.Value != creatorId)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = task.AssigneeId.Value,
                Message = $"You have been assigned the task '{task.Title}' in Workspace '{workspace.Name}' by {creator?.FullName ?? "Manager"}.",
                Type = "TaskAssignment",
                Link = $"/WorkspaceDetail/{workspace.JoinCode}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                RelatedId = task.Id
            };
            await _taskRepo.AddNotificationAsync(notification);
        }

        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        _logger.LogInformation("Task created: {Title} in Workspace {WorkspaceId}", task.Title, workspaceId);

        return null; // Success
    }

    public async Task<string?> UpdateTaskStatusAsync(Guid workspaceId, Guid userId, Guid taskId, int status)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        string userRole = userRecord?.Role ?? (workspace.OwnerId == userId ? "Manager" : "Member");

        if (userRole == "Viewer") return "Viewer role cannot update task statuses.";

        var task = await _taskRepo.GetByIdAsync(taskId);
        if (task == null || task.WorkspaceId != workspaceId) return "Task not found.";

        // Role Transition Governance:
        if (userRole != "Manager" && userRole != "Vice Manager")
        {
            if (task.AssigneeId != userId)
            {
                return "You can only move tasks assigned specifically to yourself!";
            }
            if (status == 3)
            {
                return "Only Managers or Vice Managers have permission to approve and complete tasks!";
            }
            if (task.Status == 3)
            {
                return "Only Managers or Vice Managers have permission to rework completed tasks!";
            }
        }

        int oldStatus = task.Status ?? 0;
        task.Status = status;

        var operatorUser = await _memberRepo.GetUserByIdAsync(userId);

        if (task.AssigneeId.HasValue && task.AssigneeId.Value != userId)
        {
            string msg = "";
            if (status == 3)
            {
                msg = $"Your task '{task.Title}' in Workspace '{workspace.Name}' has been APPROVED by {operatorUser?.FullName ?? "Manager"}.";
            }
            else if ((oldStatus == 2 || oldStatus == 3) && (status == 0 || status == 1))
            {
                msg = $"Your task '{task.Title}' in Workspace '{workspace.Name}' has been requested for REWORK by {operatorUser?.FullName ?? "Manager"}.";
            }

            if (!string.IsNullOrEmpty(msg))
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = task.AssigneeId.Value,
                    Message = msg,
                    Type = status == 3 ? "TaskApproved" : "TaskRework",
                    Link = $"/WorkspaceDetail/{workspace.JoinCode}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    RelatedId = task.Id
                };
                await _taskRepo.AddNotificationAsync(notification);
            }
        }

        _taskRepo.Update(task);
        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        _logger.LogInformation("Task status updated. TaskId: {TaskId}, NewStatus: {Status} by User {UserId}", taskId, status, userId);

        return null; // Success
    }

    public async Task<string?> EditTaskAsync(Guid workspaceId, Guid userId, Guid editTaskId, string editTaskTitle, string editTaskDescription, int editTaskPriority, Guid? editTaskAssigneeId, DateTime? editTaskDueDate)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        string userRole = userRecord?.Role ?? (workspace.OwnerId == userId ? "Manager" : "Member");

        bool canEdit = IsMemberAllowed(workspace, members, userId, "disabledEditTaskUsers", userRole);
        if (!canEdit) return "You do not have permission to edit tasks.";

        var task = await _taskRepo.GetByIdAsync(editTaskId);
        if (task == null || task.WorkspaceId != workspaceId) return "Task not found.";

        Guid? oldAssigneeId = task.AssigneeId;

        if (userRole == "Manager" || userRole == "Vice Manager")
        {
            if (!string.IsNullOrEmpty(editTaskTitle))
            {
                task.Title = Helpers.InputSanitizer.SanitizeInput(editTaskTitle);
            }
            task.Priority = editTaskPriority;
            task.AssigneeId = editTaskAssigneeId;

            DateTime? finalDueDate = editTaskDueDate;
            if (finalDueDate.HasValue && finalDueDate.Value.Hour == 0 && finalDueDate.Value.Minute == 0 && finalDueDate.Value.Second == 0)
            {
                finalDueDate = finalDueDate.Value.Date.AddHours(23).AddMinutes(50);
            }
            task.DueDate = finalDueDate;
        }

        task.Description = Helpers.InputSanitizer.SanitizeInput(editTaskDescription);

        _taskRepo.Update(task);

        var operatorUser = await _memberRepo.GetUserByIdAsync(userId);

        if (editTaskAssigneeId.HasValue && editTaskAssigneeId != oldAssigneeId && editTaskAssigneeId.Value != userId)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = editTaskAssigneeId.Value,
                Message = $"You have been assigned the task '{task.Title}' in Workspace '{workspace.Name}' by {operatorUser?.FullName ?? "Manager"}.",
                Type = "TaskAssignment",
                Link = $"/WorkspaceDetail/{workspace.JoinCode}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                RelatedId = task.Id
            };
            await _taskRepo.AddNotificationAsync(notification);
        }

        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        _logger.LogInformation("Task edited. TaskId: {TaskId} by User {UserId}", editTaskId, userId);

        return null; // Success
    }

    public async Task<string?> DeleteTaskAsync(Guid workspaceId, Guid userId, Guid taskId)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        string userRole = userRecord?.Role ?? (workspace.OwnerId == userId ? "Manager" : "Member");

        bool canDelete = IsMemberAllowed(workspace, members, userId, "disabledDeleteTaskUsers", userRole);
        if (!canDelete) return "You do not have permission to delete tasks.";

        var task = await _taskRepo.GetByIdAsync(taskId);
        if (task == null || task.WorkspaceId != workspaceId) return "Task not found.";

        // 1. Delete associated personal schedules to prevent foreign key errors
        var schedules = await _taskRepo.GetAssociatedSchedulesAsync(taskId);
        if (schedules.Any())
        {
            _taskRepo.RemoveSchedules(schedules);
        }

        // 2. Delete associated task comments (will cascade automatically or cleanly here)
        // EF Core will clean comments based on DbContext configure if cascade delete is enabled. Let's do it explicitly if needed, but since our repo uses cascade or EF handles it, it's safe.

        // 3. Clear file bindings (TaskId = null)
        foreach (var file in task.WorkspaceFiles)
        {
            file.TaskId = null;
        }

        _taskRepo.Remove(task);
        await _unitOfWork.SaveChangesAsync();

        // Evict cache
        _cache.Remove($"Workspace_{workspace.JoinCode}");
        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());

        // SignalR broadcast task deletion!
        var payload = new { taskId = taskId };
        await _hubContext.Clients.Group(workspaceId.ToString()).SendAsync("ReceiveTaskDeletion", payload);

        _logger.LogInformation("Task deleted: {TaskId} from Workspace {WorkspaceId} by User {UserId}", taskId, workspaceId, userId);

        return null; // Success
    }

    public async Task<string?> AddTaskCommentAsync(Guid workspaceId, Guid userId, Guid taskId, string content)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        string userRole = userRecord?.Role ?? (workspace.OwnerId == userId ? "Manager" : "Member");

        if (userRole == "Viewer") return "Viewer role cannot add comments.";
        if (string.IsNullOrEmpty(content)) return "Comment content cannot be empty.";

        var task = await _taskRepo.GetByIdAsync(taskId);
        if (task == null || task.WorkspaceId != workspaceId) return "Task not found.";

        string sanitizedContent = Helpers.InputSanitizer.SanitizeInput(content);

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            UserId = userId,
            Content = sanitizedContent,
            CreatedAt = DateTime.UtcNow
        };

        await _taskRepo.AddCommentAsync(comment);
        await _unitOfWork.SaveChangesAsync();

        _cache.Remove($"WorkspaceTasks_{workspaceId}");
        _logger.LogInformation("Comment added to task {TaskId} by {UserId}", taskId, userId);

        var user = await _memberRepo.GetUserByIdAsync(userId);
        var payload = new
        {
            id = comment.Id,
            taskId = comment.TaskId,
            userId = comment.UserId,
            userName = user?.FullName ?? "Someone",
            content = comment.Content,
            createdAt = comment.CreatedAt
        };

        await _hubContext.Clients.Group(workspaceId.ToString()).SendAsync("ReceiveTaskComment", payload);

        return null; // Success
    }

    private bool IsMemberAllowed(Workspace workspace, List<WorkspaceMember> members, Guid memberId, string key, string role)
    {
        if (workspace == null) return true;
        if (workspace.OwnerId == memberId) return true;

        var memberRecord = members.FirstOrDefault(m => m.UserId == memberId);
        if (memberRecord != null && memberRecord.Role == "Manager")
        {
            return true;
        }

        bool defaultAllowed = (role != "Viewer");

        if (!string.IsNullOrEmpty(workspace.SettingsJson))
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(workspace.SettingsJson);
                if (payload != null && payload[key] != null)
                {
                    var disabledList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(payload[key].ToJsonString());
                    if (disabledList != null)
                    {
                        return !disabledList.Contains(memberId.ToString().ToLower());
                    }
                }
            }
            catch {}
        }

        return defaultAllowed;
    }
}
