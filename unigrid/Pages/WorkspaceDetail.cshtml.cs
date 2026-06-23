using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using unigrid.Data;
using unigrid.Data.Repositories;
using unigrid.Models;
using unigrid.Services;

namespace unigrid.Pages;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
public class WorkspaceDetailModel : PageModel
{
    private readonly UniGridDbContext _context;
    private readonly IWorkspaceRepository _workspaceRepo;
    private readonly IMemberRepository _memberRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly IFileRepository _fileRepo;
    private readonly IWorkspaceService _workspaceService;
    private readonly ITaskService _taskService;
    private readonly IFileService _fileService;
    private readonly IChatService _chatService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WorkspaceDetailModel> _logger;

    public WorkspaceDetailModel(
        UniGridDbContext context,
        IWorkspaceRepository workspaceRepo,
        IMemberRepository memberRepo,
        ITaskRepository taskRepo,
        IFileRepository fileRepo,
        IWorkspaceService workspaceService,
        ITaskService taskService,
        IFileService fileService,
        IChatService chatService,
        IMemoryCache cache,
        ILogger<WorkspaceDetailModel> logger)
    {
        _context = context;
        _workspaceRepo = workspaceRepo;
        _memberRepo = memberRepo;
        _taskRepo = taskRepo;
        _fileRepo = fileRepo;
        _workspaceService = workspaceService;
        _taskService = taskService;
        _fileService = fileService;
        _chatService = chatService;
        _cache = cache;
        _logger = logger;
    }
    public Workspace Workspace { get; set; } = null!;
    public List<WorkspaceMember> Members { get; set; } = new();
    public List<unigrid.Models.Task> WorkspaceTasks { get; set; } = new();
    public List<WorkspaceFile> Files { get; set; } = new();
    public ChatRoom? ChatRoom { get; set; }
    public List<ChatMessage> ChatMessages { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? DateFilter { get; set; }

    public User CurrentUser { get; set; } = null!;
    public string UserInitials { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "Member";
    public bool ShowVisibilityControls { get; set; }

    public List<TaskCategory> WorkspaceCategories { get; set; } = new();
    public KpiReportDto WeeklyKpiReport { get; set; } = null!;
    public KpiReportDto MonthlyKpiReport { get; set; } = null!;

    [BindProperty]
    public Guid? NewTaskCategoryId { get; set; }
    [BindProperty]
    public bool NewTaskIsCounterTask { get; set; }
    [BindProperty]
    public int NewTaskTargetCount { get; set; } = 1;

    [BindProperty]
    public string NewCategoryName { get; set; } = string.Empty;
    [BindProperty]
    public string NewCategoryDescription { get; set; } = string.Empty;
    [BindProperty]
    public string NewCategoryColorHex { get; set; } = "#3B82F6";

    [BindProperty]
    public Guid NewKpiUserId { get; set; }
    [BindProperty]
    public Guid NewKpiCategoryId { get; set; }
    [BindProperty]
    public string NewKpiPeriodType { get; set; } = "Weekly";
    [BindProperty]
    public int NewKpiTargetValue { get; set; }    // Direct binding for task creation
    [BindProperty]
    public string NewTaskTitle { get; set; } = string.Empty;
    [BindProperty]
    public string NewTaskDescription { get; set; } = string.Empty;
    [BindProperty]
    public int NewTaskPriority { get; set; } = 2; // Medium default
    [BindProperty]
    public Guid? NewTaskAssigneeId { get; set; }
    [BindProperty]
    public DateTime? NewTaskDueDate { get; set; }
    [BindProperty]
    public int NewTaskStatus { get; set; } = 0; // Todo default
    [BindProperty]
    public Microsoft.AspNetCore.Http.IFormFile? NewTaskFile { get; set; }
    [BindProperty]
    public Microsoft.AspNetCore.Http.IFormFile? EditTaskFile { get; set; }

    // Direct binding for comments
    [BindProperty]
    public Guid CommentTaskId { get; set; }
    [BindProperty]
    public string CommentContent { get; set; } = string.Empty;

    // Direct binding for chat
    [BindProperty]
    public string ChatContent { get; set; } = string.Empty;

    // Direct binding for files
    [BindProperty]
    public string NewFileName { get; set; } = string.Empty;
    [BindProperty]
    public string NewFileType { get; set; } = "pdf";
    [BindProperty]
    public long NewFileSize { get; set; } = 1024;
    [BindProperty]
    public Microsoft.AspNetCore.Http.IFormFile? UploadedFile { get; set; }
    [BindProperty]
    public bool UploadIsPublic { get; set; } = true;

    // Direct binding for invites
    [BindProperty]
    public string InviteEmail { get; set; } = string.Empty;
    [BindProperty]
    public string InviteRole { get; set; } = "Member";
    [BindProperty]
    public string InviteDisplayRole { get; set; } = string.Empty;

    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync(string joinCode)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return RedirectToPage("/Login");

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile == null)
        {
            return RedirectToPage("/Profile");
        }

        var result = await LoadWorkspaceDataAsync(joinCode);
        if (!result)
        {
            return RedirectToPage("/Dashboard");
        }

        // Set sidebar workspaces list using cache
        string userWSKey = $"UserWorkspaces_{CurrentUser.Id}";
        var userWorkspaces = await _cache.GetOrCreateAsync(userWSKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _workspaceRepo.GetUserWorkspacesAsync(CurrentUser.Id);
        });
        ViewData["Workspaces"] = userWorkspaces;

        return Page();
    }

    private async System.Threading.Tasks.Task<bool> LoadWorkspaceDataAsync(string joinCode)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return false;

        var accountId = Guid.Parse(accountIdClaim);
        
        // Cache User profile
        CurrentUser = await _cache.GetOrCreateAsync($"User_{accountId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _memberRepo.GetUserByAccountIdAsync(accountId);
        });

        if (CurrentUser == null) return false;
        
        ViewData["UserName"] = CurrentUser.FullName;
        UserInitials = string.Concat(CurrentUser.FullName.Split(' ').Select(n => n[0]));
        ViewData["UserInitials"] = UserInitials;

        // Cache Workspace metadata
        Workspace = await _workspaceService.GetWorkspaceByJoinCodeAsync(joinCode);
        if (Workspace == null) return false;

        // Check plan expiration
        if (Workspace.PackageTier != "Free")
        {
            var activeBilling = await _context.Billings
                .Where(b => b.WorkspaceId == Workspace.Id && b.Status == "Active")
                .OrderByDescending(b => b.EndDate)
                .FirstOrDefaultAsync();

            if (activeBilling == null || activeBilling.EndDate < DateTime.UtcNow)
            {
                var wsToUpdate = await _context.Workspaces.FindAsync(Workspace.Id);
                if (wsToUpdate != null)
                {
                    wsToUpdate.PackageTier = "Free";
                    
                    if (activeBilling != null)
                    {
                        var billingToUpdate = await _context.Billings.FindAsync(activeBilling.Id);
                        if (billingToUpdate != null)
                        {
                            billingToUpdate.Status = "Expired";
                        }
                    }

                    var owner = await _context.Users.FindAsync(wsToUpdate.OwnerId);
                    if (owner != null && (wsToUpdate.WorkspaceType == "Personal" || owner.SubscriptionTier == "Personal"))
                    {
                        owner.SubscriptionTier = "Free";
                        owner.SubscriptionExpires = null;
                        _cache.Remove($"User_{owner.AccountId}");
                    }

                    await _context.SaveChangesAsync();

                    // Evict cache
                    _cache.Remove($"Workspace_{joinCode}");
                    _cache.Remove($"WorkspaceMembers_{Workspace.Id}");
                    
                    var memberIds = await _context.WorkspaceMembers
                        .Where(m => m.WorkspaceId == Workspace.Id)
                        .Select(m => m.UserId)
                        .ToListAsync();
                    foreach (var memberId in memberIds)
                    {
                        _cache.Remove($"UserWorkspaces_{memberId}");
                    }

                    // Reload workspace metadata
                    Workspace = await _workspaceService.GetWorkspaceByJoinCodeAsync(joinCode);
                    if (Workspace == null) return false;

                    TempData["ErrorMessage"] = "This workspace's subscription has expired and has been reverted to the Free plan. File uploads and paid features are now disabled.";
                }
            }
        }

        var workspaceId = Workspace.Id;

        // Cache Workspace Members
        Members = await _workspaceService.GetWorkspaceMembersAsync(workspaceId);

        var memberRecord = Members.FirstOrDefault(m => m.UserId == CurrentUser.Id);
        CurrentUserRole = memberRecord?.Role ?? (Workspace.OwnerId == CurrentUser.Id ? "Manager" : "Member");

        // Check if user is a member or owner
        if (Workspace.OwnerId != CurrentUser.Id && memberRecord == null)
        {
            return false;
        }

        // Cache Workspace Tasks
        WorkspaceTasks = await _cache.GetOrCreateAsync($"WorkspaceTasks_{workspaceId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _taskRepo.GetWorkspaceTasksAsync(workspaceId);
        });

        if (WorkspaceTasks != null && !string.IsNullOrEmpty(DateFilter) && DateTime.TryParse(DateFilter, out var filterDateVal))
        {
            var localDate = filterDateVal.Date;
            WorkspaceTasks = WorkspaceTasks.Where(t => t.DueDate.HasValue && t.DueDate.Value.ToLocalTime().Date == localDate).ToList();
        }

        // Cache Workspace Files
        Files = await _cache.GetOrCreateAsync($"WorkspaceFiles_{workspaceId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _fileRepo.GetWorkspaceFilesAsync(workspaceId);
        });

        // Load Categories and KPI Reports
        WorkspaceCategories = await _taskService.GetWorkspaceCategoriesAsync(workspaceId);
        WeeklyKpiReport = await _taskService.GetKpiReportAsync(workspaceId, "Weekly", DateTime.UtcNow);
        MonthlyKpiReport = await _taskService.GetKpiReportAsync(workspaceId, "Monthly", DateTime.UtcNow);

        // Load ChatRoom and ChatMessages using chat service
        ChatRoom = await _chatService.GetRoomByWorkspaceIdAsync(workspaceId);
        if (ChatRoom != null)
        {
            ChatMessages = await _chatService.GetRoomMessagesAsync(ChatRoom.Id);
        }

        return true;
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostCreateTaskAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _taskService.CreateTaskAsync(
            Workspace.Id,
            CurrentUser.Id,
            NewTaskTitle,
            NewTaskDescription,
            NewTaskPriority,
            NewTaskAssigneeId,
            NewTaskDueDate,
            NewTaskStatus,
            NewTaskCategoryId,
            NewTaskIsCounterTask,
            NewTaskTargetCount
        );

        if (error != null)
        {
            if (error.Contains("permission")) return Forbid();
            TempData["ErrorMessage"] = error;
            return RedirectToPage(new { joinCode });
        }

        // Handle file attachment if any
        if (NewTaskFile != null && NewTaskFile.Length > 0)
        {
            var tasks = await _taskRepo.GetWorkspaceTasksAsync(Workspace.Id);
            var createdTask = tasks.OrderByDescending(t => t.CreatedAt).FirstOrDefault(t => t.Title == NewTaskTitle);
            if (createdTask != null)
            {
                var uploadResult = await _fileService.UploadFileAsync(Workspace.Id, CurrentUser.Id, NewTaskFile, createdTask.Id);
                if (uploadResult.error != null)
                {
                    TempData["UploadError"] = uploadResult.error;
                }
            }
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateTaskStatusAsync(string joinCode, Guid taskId, int status)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");
        
        var error = await _taskService.UpdateTaskStatusAsync(Workspace.Id, CurrentUser.Id, taskId, status);
        if (error != null)
        {
            if (error.Contains("permission") || error.Contains("only move") || error.Contains("Forbidden")) return Forbid();
            TempData["ErrorMessage"] = error;
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostEditTaskAsync(string joinCode, Guid editTaskId, string editTaskTitle, string editTaskDescription, int editTaskPriority, Guid? editTaskAssigneeId, DateTime? editTaskDueDate, Guid? editCategoryId, bool editIsCounterTask, int editTargetCount)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _taskService.EditTaskAsync(Workspace.Id, CurrentUser.Id, editTaskId, editTaskTitle, editTaskDescription, editTaskPriority, editTaskAssigneeId, editTaskDueDate, editCategoryId, editIsCounterTask, editTargetCount);
        if (error != null)
        {
            if (error.Contains("permission")) return Forbid();
            TempData["ErrorMessage"] = error;
            return RedirectToPage(new { joinCode });
        }

        // Handle file attachment if any
        if (EditTaskFile != null && EditTaskFile.Length > 0)
        {
            var uploadResult = await _fileService.UploadFileAsync(Workspace.Id, CurrentUser.Id, EditTaskFile, editTaskId);
            if (uploadResult.error != null)
            {
                TempData["UploadError"] = uploadResult.error;
            }
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostAddTaskCommentAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Workspace not found or unauthorized." });
            }
            return RedirectToPage("/Dashboard");
        }

        var error = await _taskService.AddTaskCommentAsync(Workspace.Id, CurrentUser.Id, CommentTaskId, CommentContent);
        if (error != null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = error });
            }
            if (error.Contains("Viewer role")) return Forbid();
            TempData["ErrorMessage"] = error;
            return RedirectToPage(new { joinCode });
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var tasks = await _taskRepo.GetWorkspaceTasksAsync(Workspace.Id);
            var task = tasks.FirstOrDefault(t => t.Id == CommentTaskId);
            var comment = task?.TaskComments.OrderByDescending(c => c.CreatedAt).FirstOrDefault(c => c.UserId == CurrentUser.Id && c.Content == CommentContent);
            if (comment != null)
            {
                return new JsonResult(new
                {
                    id = comment.Id,
                    taskId = comment.TaskId,
                    userId = comment.UserId,
                    userName = CurrentUser.FullName,
                    content = comment.Content,
                    createdAt = comment.CreatedAt
                });
            }
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostSendChatMessageAsync(string joinCode, string activeChannel, Guid? selectedFileId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Workspace not found or unauthorized." });
            }
            return RedirectToPage("/Dashboard");
        }

        var result = await _chatService.SendChatMessageAsync(Workspace.Id, CurrentUser.Id, ChatContent, activeChannel, selectedFileId);
        if (result.error != null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = result.error });
            }
            if (result.error.Contains("permission") || result.error.Contains("Viewer")) return Forbid();
            TempData["ErrorMessage"] = result.error;
            return RedirectToPage(new { joinCode });
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            if (result.message != null)
            {
                string cleanContent = ChatContent;
                return new JsonResult(new
                {
                    id = result.message.Id,
                    roomId = result.message.RoomId,
                    senderId = result.message.SenderId,
                    senderName = CurrentUser.FullName,
                    content = cleanContent,
                    rawContent = result.message.Content,
                    sentAt = result.message.SentAt,
                    channel = string.IsNullOrEmpty(activeChannel) ? "general" : activeChannel
                });
            }
            return new JsonResult(new { success = true });
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUploadFileAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Workspace not found or unauthorized." });
            }
            return RedirectToPage("/Dashboard");
        }

        var result = await _fileService.UploadFileAsync(Workspace.Id, CurrentUser.Id, UploadedFile);
        if (result.error != null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = result.error });
            }
            if (result.error.Contains("Viewer")) return Forbid();
            TempData["UploadError"] = result.error;
            return RedirectToPage(new { joinCode });
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" && result.file != null)
        {
            return new JsonResult(new {
                success = true,
                file = new {
                    id = result.file.Id.ToString(),
                    fileName = result.file.FileName,
                    fileUrl = result.file.FileUrl,
                    fileSize = result.file.FileSize,
                    fileType = result.file.FileType
                }
            });
        }

        TempData["UploadSuccess"] = $"Successfully uploaded file: {UploadedFile.FileName}";
        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostInviteMemberAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _workspaceService.InviteMemberAsync(Workspace.Id, CurrentUser.Id, InviteEmail, InviteRole, InviteDisplayRole);
        if (error != null)
        {
            if (error.Contains("permission")) return Forbid();
            TempData["InviteError"] = error;
        }
        else
        {
            TempData["InviteSuccess"] = $"Invitation successfully sent to {InviteEmail}!";
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateMemberRoleAsync(string joinCode, Guid memberId, string newRole, string newDisplayRole, bool canDeleteFile, bool canCreateTask, bool canEditTask, bool canCreateChannel, bool canDeleteTask)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _workspaceService.UpdateMemberRoleAsync(Workspace.Id, CurrentUser.Id, memberId, newRole, newDisplayRole, canDeleteFile, canCreateTask, canEditTask, canCreateChannel, canDeleteTask);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
        }
        else
        {
            TempData["SuccessMessage"] = "Member role and permissions updated successfully.";
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostLeaveWorkspaceAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _workspaceService.LeaveWorkspaceAsync(Workspace.Id, CurrentUser.Id);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToPage(new { joinCode });
        }

        TempData["SuccessMessage"] = $"You have successfully left the workspace '{Workspace.Name}'.";
        return RedirectToPage("/Workspaces");
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteFileAsync(string joinCode, Guid fileId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _fileService.DeleteFileAsync(Workspace.Id, CurrentUser.Id, fileId);
        if (error != null)
        {
            if (error.Contains("permission")) return Forbid();
            TempData["ErrorMessage"] = error;
        }
        else
        {
            TempData["SuccessMessage"] = "Successfully deleted file.";
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteTaskAsync(string joinCode, Guid taskId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Workspace not found or unauthorized." });
            }
            return RedirectToPage("/Dashboard");
        }

        var error = await _taskService.DeleteTaskAsync(Workspace.Id, CurrentUser.Id, taskId);
        if (error != null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = error });
            }
            if (error.Contains("permission")) return Forbid();
            return RedirectToPage(new { joinCode });
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return new JsonResult(new { success = true, taskId = taskId });
        }

        TempData["SuccessMessage"] = "Successfully deleted task.";
        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteChatMessageAsync(string joinCode, Guid messageId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Workspace not found or unauthorized." });
            }
            return RedirectToPage("/Dashboard");
        }

        var error = await _chatService.DeleteChatMessageAsync(Workspace.Id, CurrentUser.Id, messageId);
        if (error != null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = error });
            }
            if (error.Contains("permission")) return Forbid();
            return RedirectToPage(new { joinCode });
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return new JsonResult(new { success = true, messageId = messageId });
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostTransferOwnershipAsync(string joinCode, Guid newOwnerId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _workspaceService.TransferOwnershipAsync(Workspace.Id, CurrentUser.Id, newOwnerId);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
        }
        else
        {
            TempData["SuccessMessage"] = "Successfully transferred workspace management.";
        }

        return RedirectToPage(new { joinCode });
    }

    public bool IsMemberAllowed(Guid memberId, string key, string role)
    {
        if (Workspace == null) return true;
        if (Workspace.OwnerId == memberId) return true;

        var memberRecord = Members?.FirstOrDefault(m => m.UserId == memberId);
        if (memberRecord != null && memberRecord.Role == "Manager")
        {
            return true;
        }

        bool defaultAllowed = (role != "Viewer");

        if (!string.IsNullOrEmpty(Workspace.SettingsJson))
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(Workspace.SettingsJson);
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

    public bool IsMemberAllowedToCreateChannel(Guid memberId)
    {
        var role = Members?.FirstOrDefault(m => m.UserId == memberId)?.Role ?? "Member";
        return IsMemberAllowed(memberId, "disabledCreateChannelUsers", role);
    }

    public string GetLockedChannelsJson()
    {
        if (Workspace == null || string.IsNullOrEmpty(Workspace.SettingsJson)) return "{}";
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(Workspace.SettingsJson);
            if (node != null && node["lockedChannels"] != null)
            {
                return node["lockedChannels"].ToJsonString();
            }
        }
        catch {}
        return "{}";
    }

    public string GetChannelOwnersJson()
    {
        if (Workspace == null || string.IsNullOrEmpty(Workspace.SettingsJson)) return "{}";
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(Workspace.SettingsJson);
            if (node != null && node["channelOwners"] != null)
            {
                return node["channelOwners"].ToJsonString();
            }
        }
        catch {}
        return "{}";
    }

    public string GetChannelModeratorsJson()
    {
        if (Workspace == null || string.IsNullOrEmpty(Workspace.SettingsJson)) return "{}";
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(Workspace.SettingsJson);
            if (node != null && node["channelModerators"] != null)
            {
                return node["channelModerators"].ToJsonString();
            }
        }
        catch {}
        return "{}";
    }

    public string GetChannelsJson()
    {
        if (Workspace == null || string.IsNullOrEmpty(Workspace.SettingsJson)) return "[\"general\"]";
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(Workspace.SettingsJson);
            if (node != null && node["allChannels"] != null)
            {
                return node["allChannels"].ToJsonString();
            }
        }
        catch {}
        return "[\"general\"]";
    }

    public string SerializeTask(unigrid.Models.Task task)
    {
        var finalDueDate = task.DueDate;
        if (finalDueDate.HasValue && finalDueDate.Value.Hour == 0 && finalDueDate.Value.Minute == 0 && finalDueDate.Value.Second == 0)
        {
            finalDueDate = finalDueDate.Value.Date.AddHours(23).AddMinutes(50);
        }
        var cleanTask = new {
            id = task.Id,
            title = task.Title,
            description = task.Description,
            status = task.Status,
            priority = task.Priority,
            dueDate = finalDueDate,
            categoryId = task.CategoryId,
            isCounterTask = task.IsCounterTask,
            targetCount = task.TargetCount,
            currentCount = task.CurrentCount,
            assignee = task.Assignee != null ? new { id = task.Assignee.Id, fullName = task.Assignee.FullName } : null,
            taskComments = task.TaskComments.Select(tc => new {
                id = tc.Id,
                content = tc.Content,
                createdAt = tc.CreatedAt,
                user = new { fullName = tc.User.FullName }
            }).ToList()
        };
        return System.Text.Json.JsonSerializer.Serialize(cleanTask, new System.Text.Json.JsonSerializerOptions {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }

    public string SerializeTaskBase64(unigrid.Models.Task task)
    {
        var json = SerializeTask(task);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public string SerializeFile(WorkspaceFile file)
    {
        var cleanFile = new {
            id = file.Id,
            fileName = file.FileName,
            fileUrl = file.FileUrl,
            fileType = file.FileType,
            fileSize = file.FileSize,
            isPublic = file.IsPublic,
            createdAt = file.CreatedAt,
            user = new { fullName = file.User.FullName }
        };
        return System.Text.Json.JsonSerializer.Serialize(cleanFile, new System.Text.Json.JsonSerializerOptions {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }

    public string SerializeFileBase64(WorkspaceFile file)
    {
        var json = SerializeFile(file);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public string SerializeChatMessages()
    {
        var cleanMessages = ChatMessages.Select(cm => {
            string channel = "general";
            string cleanContent = cm.Content;
            
            if (cm.Content.StartsWith("[channel:"))
            {
                var endIndex = cm.Content.IndexOf("]");
                if (endIndex > 9)
                {
                    channel = cm.Content.Substring(9, endIndex - 9);
                    cleanContent = cm.Content.Substring(endIndex + 1);
                }
            }
            
            if (cleanContent.StartsWith("[file:"))
            {
                var fileEndIndex = cleanContent.IndexOf("]");
                if (fileEndIndex > 6)
                {
                    cleanContent = cleanContent.Substring(fileEndIndex + 1);
                }
            }

            return new {
                id = cm.Id,
                roomId = cm.RoomId,
                senderId = cm.SenderId,
                senderName = cm.Sender.FullName,
                content = cleanContent,
                rawContent = cm.Content,
                sentAt = cm.SentAt,
                channel = channel
            };
        }).ToList();

        return System.Text.Json.JsonSerializer.Serialize(cleanMessages, new System.Text.Json.JsonSerializerOptions {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }

    public string SerializeChatMessagesBase64()
    {
        var json = SerializeChatMessages();
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostCreateCategoryAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _taskService.CreateCategoryAsync(Workspace.Id, CurrentUser.Id, NewCategoryName, NewCategoryDescription, NewCategoryColorHex);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
        }
        else
        {
            TempData["SuccessMessage"] = "Task category was created successfully.";
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteCategoryAsync(string joinCode, Guid categoryId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _taskService.DeleteCategoryAsync(Workspace.Id, CurrentUser.Id, categoryId);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
        }
        else
        {
            TempData["SuccessMessage"] = "Task category has been deleted.";
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostCreateKpiTargetAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var planSetting = AdminSettings.GetPlanSetting(Workspace.PackageTier, _context);
        if (!planSetting.HasAdvancedAnalytics)
        {
            TempData["ErrorMessage"] = "Advanced KPI target analytics is not supported on your workspace plan. Please upgrade to Pro+ or higher.";
            return RedirectToPage(new { joinCode });
        }

        // Compute startDate and endDate based on periodType
        DateTime startDate = DateTime.UtcNow;
        DateTime endDate = DateTime.UtcNow;

        if (NewKpiPeriodType == "Weekly")
        {
            // Start of current week (Monday)
            int diff = (7 + (startDate.DayOfWeek - DayOfWeek.Monday)) % 7;
            startDate = startDate.AddDays(-1 * diff).Date;
            endDate = startDate.AddDays(7).AddTicks(-1);
        }
        else // Monthly
        {
            startDate = new DateTime(startDate.Year, startDate.Month, 1);
            endDate = startDate.AddMonths(1).AddTicks(-1);
        }

        var error = await _taskService.CreateKpiTargetAsync(
            Workspace.Id,
            CurrentUser.Id,
            NewKpiUserId,
            NewKpiCategoryId,
            NewKpiPeriodType,
            startDate,
            endDate,
            NewKpiTargetValue
        );

        if (error != null)
        {
            TempData["ErrorMessage"] = error;
        }
        else
        {
            TempData["SuccessMessage"] = "KPI target has been set successfully.";
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteKpiTargetAsync(string joinCode, Guid targetId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var planSetting = AdminSettings.GetPlanSetting(Workspace.PackageTier, _context);
        if (!planSetting.HasAdvancedAnalytics)
        {
            TempData["ErrorMessage"] = "Advanced KPI target analytics is not supported on your workspace plan. Please upgrade to Pro+ or higher.";
            return RedirectToPage(new { joinCode });
        }

        var error = await _taskService.DeleteKpiTargetAsync(Workspace.Id, CurrentUser.Id, targetId);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
        }
        else
        {
            TempData["SuccessMessage"] = "KPI target has been deleted.";
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateTaskCounterAsync(string joinCode, Guid taskId, int currentCount)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var error = await _taskService.UpdateTaskCounterAsync(Workspace.Id, CurrentUser.Id, taskId, currentCount);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
        }

        return RedirectToPage(new { joinCode });
    }
}
