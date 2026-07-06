using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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
    private readonly unigrid.Data.UniGridDbContext _context;
    private readonly INotificationService _notificationService;

    public TaskService(
        IWorkspaceRepository workspaceRepo,
        IMemberRepository memberRepo,
        ITaskRepository taskRepo,
        IWorkspaceService workspaceService,
        IUnitOfWork unitOfWork,
        IMemoryCache cache,
        IHubContext<ChatHub> hubContext,
        ILogger<TaskService> logger,
        unigrid.Data.UniGridDbContext context,
        INotificationService notificationService)
    {
        _workspaceRepo = workspaceRepo;
        _memberRepo = memberRepo;
        _taskRepo = taskRepo;
        _workspaceService = workspaceService;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _hubContext = hubContext;
        _logger = logger;
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<string?> CreateTaskAsync(Guid workspaceId, Guid creatorId, string title, string description, int priority, Guid? assigneeId, DateTime? dueDate, int status, Guid? categoryId = null, bool isCounterTask = false, int targetCount = 1)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var creatorRecord = members.FirstOrDefault(m => m.UserId == creatorId);
        if (workspace.OwnerId != creatorId && creatorRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string creatorRole = creatorRecord?.Role ?? "Manager";

        bool canCreate = IsMemberAllowed(workspace, members, creatorId, "disabledCreateTaskUsers", creatorRole);
        if (!canCreate) return "You do not have permission to create tasks.";

        if (string.IsNullOrEmpty(title)) return "Task title is required.";

        string sanitizedTitle = Helpers.InputSanitizer.SanitizeInput(title);
        string sanitizedDescription = Helpers.InputSanitizer.SanitizeInput(description);

        DateTime? finalDueDate = dueDate;
        if (finalDueDate.HasValue)
        {
            if (finalDueDate.Value.Hour == 0 && finalDueDate.Value.Minute == 0 && finalDueDate.Value.Second == 0)
            {
                finalDueDate = finalDueDate.Value.Date.AddHours(23).AddMinutes(50);
            }
            if (finalDueDate.Value.Kind == DateTimeKind.Unspecified)
            {
                finalDueDate = DateTime.SpecifyKind(finalDueDate.Value, DateTimeKind.Utc);
            }
            else if (finalDueDate.Value.Kind == DateTimeKind.Local)
            {
                finalDueDate = finalDueDate.Value.ToUniversalTime();
            }
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
            CreatedAt = DateTime.UtcNow,
            CategoryId = categoryId,
            IsCounterTask = isCounterTask,
            TargetCount = targetCount,
            CurrentCount = 0
        };

        await _taskRepo.AddAsync(task);

        var creator = await _memberRepo.GetUserByIdAsync(creatorId);

        await _unitOfWork.SaveChangesAsync();

        if (task.AssigneeId.HasValue && task.AssigneeId.Value != creatorId)
        {
            await _notificationService.CreateAndSendNotificationAsync(
                task.AssigneeId.Value,
                $"You have been assigned the task '{task.Title}' in Workspace '{workspace.Name}' by {creator?.FullName ?? "Manager"}.",
                "TaskAssignment",
                $"/WorkspaceDetail/{workspace.JoinCode}",
                task.Id
            );
        }

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        _logger.LogInformation("Task created: {Title} in Workspace {WorkspaceId}", task.Title, workspaceId);

        return null; // Success
    }

    public async Task<string?> UpdateTaskStatusAsync(Guid? workspaceId, Guid userId, Guid taskId, int status)
    {
        var task = await _taskRepo.GetByIdAsync(taskId);
        if (task == null) return "Task not found.";

        Guid? resolvedWorkspaceId = workspaceId ?? task.WorkspaceId;

        if (resolvedWorkspaceId.HasValue)
        {
            var workspace = await _workspaceRepo.GetByIdAsync(resolvedWorkspaceId.Value);
            if (workspace == null) return "Workspace not found.";

            var members = await _memberRepo.GetWorkspaceMembersAsync(resolvedWorkspaceId.Value);
            var userRecord = members.FirstOrDefault(m => m.UserId == userId);
            if (workspace.OwnerId != userId && userRecord == null)
            {
                return "Access denied to this workspace.";
            }
            string userRole = userRecord?.Role ?? "Manager";

            if (userRole == "Viewer") return "Viewer role cannot update task statuses.";
            if (task.WorkspaceId != resolvedWorkspaceId.Value) return "Task not found in workspace.";

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

            _taskRepo.Update(task);
            await _unitOfWork.SaveChangesAsync();

            // Task submitted for review: Notify workspace owner
            if (status == 2 && oldStatus != 2)
            {
                var reviewMsg = $"Task '{task.Title}' in Workspace '{workspace.Name}' has been submitted for review by {operatorUser?.FullName ?? "Member"}.";
                await _notificationService.CreateAndSendNotificationAsync(
                    workspace.OwnerId,
                    reviewMsg,
                    "TaskReviewRequest",
                    $"/WorkspaceDetail/{workspace.JoinCode}",
                    task.Id
                );
            }

            // Task approved or rework requested: Notify assignee
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
                    await _notificationService.CreateAndSendNotificationAsync(
                        task.AssigneeId.Value,
                        msg,
                        status == 3 ? "TaskApproved" : "TaskRework",
                        $"/WorkspaceDetail/{workspace.JoinCode}",
                        task.Id
                    );
                }
            }

            _workspaceService.EvictWorkspaceCache(resolvedWorkspaceId.Value, members.Select(m => m.UserId).ToList());
        }
        else if (task.FederationId.HasValue)
        {
            // Federation-level task
            var federation = await _context.WorkspaceFederations
                .Include(f => f.WorkspaceFederationMembers)
                .FirstOrDefaultAsync(f => f.Id == task.FederationId.Value);

            if (federation == null) return "Federation not found.";

            var fedMember = federation.WorkspaceFederationMembers.FirstOrDefault(m => m.UserId == userId);
            bool isOwner = federation.OwnerId == userId;
            bool isActiveMember = fedMember != null && fedMember.Status == "Active";

            if (!isOwner && !isActiveMember)
            {
                return "You are not an active member of this Federation.";
            }

            string userRole = isOwner ? "Owner" : fedMember.Role;

            // Restrict status changes for managers/presidents vs normal members
            if (userRole != "Owner" && userRole != "HeadPresident" && userRole != "DepartmentManager")
            {
                if (task.AssigneeId != userId)
                {
                    return "You can only update tasks assigned to yourself.";
                }
            }

            task.Status = status;
            _taskRepo.Update(task);
            await _unitOfWork.SaveChangesAsync();
        }
        else
        {
            return "Task is not associated with any workspace or federation.";
        }

        _logger.LogInformation("Task status updated. TaskId: {TaskId}, NewStatus: {Status} by User {UserId}", taskId, status, userId);
        return null; // Success
    }

    public async Task<string?> EditTaskAsync(Guid workspaceId, Guid userId, Guid editTaskId, string editTaskTitle, string editTaskDescription, int editTaskPriority, Guid? editTaskAssigneeId, DateTime? editTaskDueDate, Guid? editCategoryId = null, bool editIsCounterTask = false, int editTargetCount = 1)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string userRole = userRecord?.Role ?? "Manager";

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
            if (finalDueDate.HasValue)
            {
                if (finalDueDate.Value.Hour == 0 && finalDueDate.Value.Minute == 0 && finalDueDate.Value.Second == 0)
                {
                    finalDueDate = finalDueDate.Value.Date.AddHours(23).AddMinutes(50);
                }
                if (finalDueDate.Value.Kind == DateTimeKind.Unspecified)
                {
                    finalDueDate = DateTime.SpecifyKind(finalDueDate.Value, DateTimeKind.Utc);
                }
                else if (finalDueDate.Value.Kind == DateTimeKind.Local)
                {
                    finalDueDate = finalDueDate.Value.ToUniversalTime();
                }
            }
            task.DueDate = finalDueDate;
            task.CategoryId = editCategoryId;
            task.IsCounterTask = editIsCounterTask;
            task.TargetCount = editTargetCount;
        }

        task.Description = Helpers.InputSanitizer.SanitizeInput(editTaskDescription);

        _taskRepo.Update(task);

        var operatorUser = await _memberRepo.GetUserByIdAsync(userId);

        await _unitOfWork.SaveChangesAsync();

        if (editTaskAssigneeId.HasValue && editTaskAssigneeId != oldAssigneeId && editTaskAssigneeId.Value != userId)
        {
            await _notificationService.CreateAndSendNotificationAsync(
                editTaskAssigneeId.Value,
                $"You have been assigned the task '{task.Title}' in Workspace '{workspace.Name}' by {operatorUser?.FullName ?? "Manager"}.",
                "TaskAssignment",
                $"/WorkspaceDetail/{workspace.JoinCode}",
                task.Id
            );
        }

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
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string userRole = userRecord?.Role ?? "Manager";

        bool canDelete = IsMemberAllowed(workspace, members, userId, "disabledDeleteTaskUsers", userRole);
        if (!canDelete) return "You do not have permission to delete tasks.";

        var task = await _taskRepo.GetByIdAsync(taskId);
        if (task == null || task.WorkspaceId != workspaceId) return "Task not found.";

        // 1. Task can only be deleted if it is not attached to a schedule and its status is To Do (0)
        var schedules = await _taskRepo.GetAssociatedSchedulesAsync(taskId);
        if (schedules.Any())
        {
            return "Tasks can only be deleted if they are not attached to a schedule and their status is To Do.";
        }

        if (task.Status.HasValue && task.Status.Value != 0)
        {
            return "Tasks can only be deleted if they are not attached to a schedule and their status is To Do.";
        }

        // 2. Clear file bindings (TaskId = null)
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
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string userRole = userRecord?.Role ?? "Manager";

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

        if (task.AssigneeId.HasValue && task.AssigneeId.Value != userId)
        {
            var commenterName = user?.FullName ?? "A member";
            var truncatedContent = content.Length > 60 ? content.Substring(0, 57) + "..." : content;
            var msg = $"{commenterName} commented on your task '{task.Title}': \"{truncatedContent}\"";
            await _notificationService.CreateAndSendNotificationAsync(
                task.AssigneeId.Value,
                msg,
                "TaskComment",
                $"/WorkspaceDetail/{workspace.JoinCode}",
                task.Id
            );
        }

        // Scan for mentions and send notifications
        if (!string.IsNullOrEmpty(content))
        {
            var senderName = user?.FullName ?? "Someone";
            foreach (var m in members)
            {
                if (m.UserId == userId) continue;
                if (task.AssigneeId.HasValue && m.UserId == task.AssigneeId.Value) continue; // Already notified as assignee

                string mentionTag = $"@{m.User.FullName}";
                if (content.Contains(mentionTag, StringComparison.OrdinalIgnoreCase))
                {
                    var truncatedContent = content.Length > 60 ? content.Substring(0, 57) + "..." : content;
                    
                    // Clean up any [file:...] tag
                    var displayContent = truncatedContent;
                    var fileRegexForMention = new System.Text.RegularExpressions.Regex(@"\[file:[a-f0-9-]{36}\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    displayContent = fileRegexForMention.Replace(displayContent, "").Trim();

                    var notificationMessage = $"{senderName} mentioned you in comments on task '{task.Title}': \"{displayContent}\"";
                    await _notificationService.CreateAndSendNotificationAsync(
                        m.UserId,
                        notificationMessage,
                        "TaskCommentMention",
                        $"/WorkspaceDetail/{workspace.JoinCode}",
                        task.Id
                    );
                }
            }
        }
        object? filePayload = null;
        var fileRegex = new System.Text.RegularExpressions.Regex(@"\[file:([a-f0-9-]{36})\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var match = fileRegex.Match(content);
        if (match.Success && Guid.TryParse(match.Groups[1].Value, out var fileId))
        {
            var dbFile = await _context.WorkspaceFiles.FindAsync(fileId);
            if (dbFile != null)
            {
                filePayload = new {
                    id = dbFile.Id.ToString(),
                    fileName = dbFile.FileName,
                    fileUrl = dbFile.FileUrl,
                    fileSize = dbFile.FileSize,
                    fileType = dbFile.FileType
                };
            }
        }

        var payload = new
        {
            id = comment.Id,
            taskId = comment.TaskId,
            userId = comment.UserId,
            userName = user?.FullName ?? "Someone",
            content = comment.Content,
            createdAt = comment.CreatedAt,
            uploadedFile = filePayload
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

    // Counter task counter update
    public async Task<string?> UpdateTaskCounterAsync(Guid? workspaceId, Guid userId, Guid taskId, int currentCount)
    {
        var task = await _taskRepo.GetByIdAsync(taskId);
        if (task == null) return "Task not found.";

        Guid? resolvedWorkspaceId = workspaceId ?? task.WorkspaceId;

        if (resolvedWorkspaceId.HasValue)
        {
            var workspace = await _workspaceRepo.GetByIdAsync(resolvedWorkspaceId.Value);
            if (workspace == null) return "Workspace not found.";

            var members = await _memberRepo.GetWorkspaceMembersAsync(resolvedWorkspaceId.Value);
            var userRecord = members.FirstOrDefault(m => m.UserId == userId);
            if (workspace.OwnerId != userId && userRecord == null)
            {
                return "Access denied to this workspace.";
            }
            string userRole = userRecord?.Role ?? "Manager";

            if (userRole == "Viewer") return "Viewer role cannot update task counters.";
            if (task.WorkspaceId != resolvedWorkspaceId.Value) return "Task not found in workspace.";
            if (!task.IsCounterTask) return "This task is not a counter task.";
            if (currentCount < 0) return "Counter value cannot be negative.";
            if (currentCount > task.TargetCount) return $"Counter value cannot exceed target count of {task.TargetCount}.";

            task.CurrentCount = currentCount;
            if (currentCount > 0 && task.Status == 0)
            {
                task.Status = 1;
            }

            _taskRepo.Update(task);
            await _unitOfWork.SaveChangesAsync();

            _workspaceService.EvictWorkspaceCache(resolvedWorkspaceId.Value, members.Select(m => m.UserId).ToList());
            _cache.Remove($"WorkspaceTasks_{resolvedWorkspaceId.Value}");

            // Real-time broadcast
            var payload = new
            {
                taskId = task.Id,
                currentCount = task.CurrentCount,
                targetCount = task.TargetCount,
                status = task.Status,
                userId = userId
            };
            await _hubContext.Clients.Group(resolvedWorkspaceId.Value.ToString()).SendAsync("ReceiveTaskCounterUpdate", payload);
        }
        else if (task.FederationId.HasValue)
        {
            var federation = await _context.WorkspaceFederations
                .Include(f => f.WorkspaceFederationMembers)
                .FirstOrDefaultAsync(f => f.Id == task.FederationId.Value);

            if (federation == null) return "Federation not found.";

            var fedMember = federation.WorkspaceFederationMembers.FirstOrDefault(m => m.UserId == userId);
            bool isOwner = federation.OwnerId == userId;
            bool isActiveMember = fedMember != null && fedMember.Status == "Active";

            if (!isOwner && !isActiveMember)
            {
                return "You are not an active member of this Federation.";
            }

            if (!task.IsCounterTask) return "This task is not a counter task.";
            if (currentCount < 0) return "Counter value cannot be negative.";
            if (currentCount > task.TargetCount) return $"Counter value cannot exceed target count of {task.TargetCount}.";

            task.CurrentCount = currentCount;
            if (currentCount > 0 && task.Status == 0)
            {
                task.Status = 1;
            }

            _taskRepo.Update(task);
            await _unitOfWork.SaveChangesAsync();
        }
        else
        {
            return "Task is not associated with any workspace or federation.";
        }

        _logger.LogInformation("Task counter updated. TaskId: {TaskId}, Count: {Count}/{Target} by User {UserId}", taskId, currentCount, task.TargetCount, userId);
        return null;
    }

    // TaskCategory methods
    public async Task<List<TaskCategory>> GetWorkspaceCategoriesAsync(Guid workspaceId)
    {
        return await _taskRepo.GetWorkspaceCategoriesAsync(workspaceId);
    }

    public async Task<string?> CreateCategoryAsync(Guid workspaceId, Guid userId, string name, string? description, string colorHex)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string userRole = userRecord?.Role ?? "Manager";

        if (userRole != "Manager" && userRole != "Vice Manager")
        {
            return "Only Managers or Vice Managers have permission to create categories.";
        }

        if (string.IsNullOrEmpty(name)) return "Category name is required.";

        var planSetting = AdminSettings.GetPlanSetting(workspace.PackageTier, _context);
        if (planSetting.TaskBranchLimit >= 0)
        {
            var categories = await _taskRepo.GetWorkspaceCategoriesAsync(workspaceId);
            if (categories != null && categories.Count >= planSetting.TaskBranchLimit)
            {
                return $"Your workspace has reached the limit of {planSetting.TaskBranchLimit} task categories (branches) allowed on the {planSetting.Name} plan. Please upgrade your workspace package to create more task branches.";
            }
        }

        var category = new TaskCategory
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = Helpers.InputSanitizer.SanitizeInput(name),
            Description = Helpers.InputSanitizer.SanitizeInput(description),
            ColorHex = colorHex ?? "#3B82F6",
            CreatedAt = DateTime.UtcNow
        };

        await _taskRepo.AddCategoryAsync(category);
        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        return null; // Success
    }

    public async Task<string?> UpdateCategoryAsync(Guid workspaceId, Guid userId, Guid categoryId, string name, string? description, string colorHex)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string userRole = userRecord?.Role ?? "Manager";

        if (userRole != "Manager" && userRole != "Vice Manager")
        {
            return "Only Managers or Vice Managers have permission to update categories.";
        }

        var category = await _taskRepo.GetCategoryByIdAsync(categoryId);
        if (category == null || category.WorkspaceId != workspaceId) return "Category not found.";

        if (string.IsNullOrEmpty(name)) return "Category name is required.";

        category.Name = Helpers.InputSanitizer.SanitizeInput(name);
        category.Description = Helpers.InputSanitizer.SanitizeInput(description);
        category.ColorHex = colorHex ?? "#3B82F6";

        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        return null; // Success
    }

    public async Task<string?> DeleteCategoryAsync(Guid workspaceId, Guid userId, Guid categoryId)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string userRole = userRecord?.Role ?? "Manager";

        if (userRole != "Manager" && userRole != "Vice Manager")
        {
            return "Only Managers or Vice Managers have permission to delete categories.";
        }

        var category = await _taskRepo.GetCategoryByIdAsync(categoryId);
        if (category == null || category.WorkspaceId != workspaceId) return "Category not found.";

        _taskRepo.RemoveCategory(category);
        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        return null; // Success
    }

    // KpiTarget methods
    public async Task<List<KpiTarget>> GetWorkspaceTargetsAsync(Guid workspaceId)
    {
        return await _taskRepo.GetWorkspaceTargetsAsync(workspaceId);
    }

    public async Task<string?> CreateKpiTargetAsync(Guid workspaceId, Guid creatorId, Guid userId, Guid categoryId, string periodType, DateTime startDate, DateTime endDate, int targetValue)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var creatorRecord = members.FirstOrDefault(m => m.UserId == creatorId);
        if (workspace.OwnerId != creatorId && creatorRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string creatorRole = creatorRecord?.Role ?? "Manager";

        if (creatorRole != "Manager" && creatorRole != "Vice Manager")
        {
            return "Only Managers or Vice Managers have permission to set KPI targets.";
        }

        // Validate target member belongs to this workspace
        var targetMember = members.FirstOrDefault(m => m.UserId == userId);
        if (targetMember == null && workspace.OwnerId != userId)
        {
            return "Target user is not a member of this workspace.";
        }

        var category = await _taskRepo.GetCategoryByIdAsync(categoryId);
        if (category == null || category.WorkspaceId != workspaceId)
        {
            return "Task category not found in this workspace.";
        }

        if (targetValue <= 0) return "KPI target value must be greater than zero.";

        DateTime finalStartDate = startDate;
        DateTime finalEndDate = endDate;
        if (finalStartDate.Kind == DateTimeKind.Unspecified)
        {
            finalStartDate = DateTime.SpecifyKind(finalStartDate, DateTimeKind.Utc);
        }
        else if (finalStartDate.Kind == DateTimeKind.Local)
        {
            finalStartDate = finalStartDate.ToUniversalTime();
        }
        if (finalEndDate.Kind == DateTimeKind.Unspecified)
        {
            finalEndDate = DateTime.SpecifyKind(finalEndDate, DateTimeKind.Utc);
        }
        else if (finalEndDate.Kind == DateTimeKind.Local)
        {
            finalEndDate = finalEndDate.ToUniversalTime();
        }

        var target = new KpiTarget
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            CategoryId = categoryId,
            PeriodType = periodType,
            StartDate = finalStartDate,
            EndDate = finalEndDate,
            TargetValue = targetValue,
            CreatedAt = DateTime.UtcNow
        };

        await _taskRepo.AddTargetAsync(target);
        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        return null; // Success
    }

    public async Task<string?> DeleteKpiTargetAsync(Guid workspaceId, Guid userId, Guid targetId)
    {
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        if (workspace == null) return "Workspace not found.";

        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
        var userRecord = members.FirstOrDefault(m => m.UserId == userId);
        if (workspace.OwnerId != userId && userRecord == null)
        {
            return "Access denied to this workspace.";
        }
        string userRole = userRecord?.Role ?? "Manager";

        if (userRole != "Manager" && userRole != "Vice Manager")
        {
            return "Only Managers or Vice Managers have permission to delete KPI targets.";
        }

        var target = await _taskRepo.GetTargetByIdAsync(targetId);
        if (target == null || target.WorkspaceId != workspaceId) return "KPI target not found.";

        _taskRepo.RemoveTarget(target);
        await _unitOfWork.SaveChangesAsync();

        _workspaceService.EvictWorkspaceCache(workspaceId, members.Select(m => m.UserId).ToList());
        return null; // Success
    }

    public async Task<KpiReportDto> GetKpiReportAsync(Guid workspaceId, string periodType, DateTime targetDate)
    {
        DateTime startDate;
        DateTime endDate;

        // Calculate time range in UTC based on periodType
        if (periodType.Equals("Daily", StringComparison.OrdinalIgnoreCase))
        {
            startDate = targetDate.Date;
            endDate = startDate.AddDays(1).AddTicks(-1);
        }
        else if (periodType.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
        {
            int diff = (7 + (targetDate.DayOfWeek - DayOfWeek.Monday)) % 7;
            startDate = targetDate.AddDays(-1 * diff).Date;
            endDate = startDate.AddDays(7).AddTicks(-1);
        }
        else // Monthly as fallback/default
        {
            periodType = "Monthly";
            startDate = new DateTime(targetDate.Year, targetDate.Month, 1);
            endDate = startDate.AddMonths(1).AddTicks(-1);
        }

        var categories = await _taskRepo.GetWorkspaceCategoriesAsync(workspaceId);
        var targets = await _taskRepo.GetWorkspaceTargetsAsync(workspaceId);
        var tasks = await _taskRepo.GetWorkspaceTasksAsync(workspaceId);
        var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);

        // Include the workspace owner as a potential member to track
        var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
        var usersToTrack = new List<User>();
        
        foreach (var m in members)
        {
            var user = await _memberRepo.GetUserByIdAsync(m.UserId);
            if (user != null) usersToTrack.Add(user);
        }

        if (workspace != null && !usersToTrack.Any(u => u.Id == workspace.OwnerId))
        {
            var owner = await _memberRepo.GetUserByIdAsync(workspace.OwnerId);
            if (owner != null) usersToTrack.Insert(0, owner);
        }

        var report = new KpiReportDto
        {
            PeriodType = periodType,
            StartDate = startDate,
            EndDate = endDate,
            Categories = categories.Select(c => new KpiCategoryDto
            {
                CategoryId = c.Id,
                CategoryName = c.Name,
                ColorHex = c.ColorHex
            }).ToList()
        };

        // Filter KPI targets that apply to this specific period
        var activeTargets = targets.Where(t => t.PeriodType.Equals(periodType, StringComparison.OrdinalIgnoreCase) && 
                                              t.StartDate >= startDate && 
                                              t.EndDate <= endDate).ToList();

        // Filter tasks whose due date falls in this period
        var activeTasks = tasks.Where(t => t.DueDate.HasValue && 
                                           t.DueDate.Value >= startDate && 
                                           t.DueDate.Value <= endDate).ToList();

        foreach (var user in usersToTrack)
        {
            var perf = new MemberPerformanceDto
            {
                UserId = user.Id,
                FullName = user.FullName ?? "Unknown User",
                AvatarUrl = user.AvatarUrl
            };

            foreach (var cat in categories)
            {
                // Find targets set for this user + category in this period
                var userCatTarget = activeTargets.FirstOrDefault(t => t.UserId == user.Id && t.CategoryId == cat.Id);
                int targetValue = userCatTarget?.TargetValue ?? 0;

                // Sum completed regular tasks or count completed items in counter tasks
                var userCatTasks = activeTasks.Where(t => t.AssigneeId == user.Id && t.CategoryId == cat.Id).ToList();
                int actualValue = 0;

                foreach (var task in userCatTasks)
                {
                    if (task.IsCounterTask)
                    {
                        actualValue += task.CurrentCount;
                    }
                    else if (task.Status == 3) // Completed regular tasks
                    {
                        actualValue += 1;
                    }
                }

                double achievementRate = targetValue > 0 ? (actualValue * 100.0 / targetValue) : 0.0;

                perf.KpiDetails.Add(new KpiDetailDto
                {
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    TargetValue = targetValue,
                    ActualValue = actualValue,
                    AchievementRate = Math.Round(achievementRate, 1)
                });
            }

            // Overall completion rate: average of achievement rates of targeted categories
            var targetedCats = perf.KpiDetails.Where(d => d.TargetValue > 0).ToList();
            double totalRate = targetedCats.Any() ? targetedCats.Average(d => d.AchievementRate) : 0.0;
            perf.TotalAchievementRate = Math.Round(totalRate, 1);

            report.MemberPerformances.Add(perf);
        }

        // Sort members by total achievement rate descending
        report.MemberPerformances = report.MemberPerformances.OrderByDescending(mp => mp.TotalAchievementRate).ToList();

        return report;
    }
}
