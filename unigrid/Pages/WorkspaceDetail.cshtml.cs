using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;

namespace unigrid.Pages;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
public class WorkspaceDetailModel : PageModel
{
    private readonly UniGridDbContext _context;
    private readonly ILogger<WorkspaceDetailModel> _logger;
    private readonly IHubContext<unigrid.Hubs.ChatHub> _hubContext;
    private readonly IMemoryCache _cache;

    public WorkspaceDetailModel(UniGridDbContext context, ILogger<WorkspaceDetailModel> logger, IHubContext<unigrid.Hubs.ChatHub> hubContext, IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _hubContext = hubContext;
        _cache = cache;
    }

    public Workspace Workspace { get; set; } = null!;
    public List<WorkspaceMember> Members { get; set; } = new();
    public List<unigrid.Models.Task> WorkspaceTasks { get; set; } = new();
    public List<WorkspaceFile> Files { get; set; } = new();
    public ChatRoom? ChatRoom { get; set; }
    public List<ChatMessage> ChatMessages { get; set; } = new();

    public User CurrentUser { get; set; } = null!;
    public string UserInitials { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "Member";
    public bool ShowVisibilityControls { get; set; }

    // Direct binding for task creation
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
            return await _context.Workspaces
                .Where(w => w.OwnerId == CurrentUser.Id || w.WorkspaceMembers.Any(m => m.UserId == CurrentUser.Id))
                .ToListAsync();
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
            return await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        });

        if (CurrentUser == null) return false;
        
        ViewData["UserName"] = CurrentUser.FullName;
        UserInitials = string.Concat(CurrentUser.FullName.Split(' ').Select(n => n[0]));
        ViewData["UserInitials"] = UserInitials;

        // Cache Workspace metadata
        Workspace = await _cache.GetOrCreateAsync($"Workspace_{joinCode}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _context.Workspaces
                .Include(w => w.Owner)
                .FirstOrDefaultAsync(w => w.JoinCode == joinCode);
        });

        if (Workspace == null) return false;

        var workspaceId = Workspace.Id;

        // Cache Workspace Members
        Members = await _cache.GetOrCreateAsync($"WorkspaceMembers_{workspaceId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _context.WorkspaceMembers
                .Include(wm => wm.User)
                .Where(wm => wm.WorkspaceId == workspaceId)
                .ToListAsync();
        });

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
            return await _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.WorkspaceFiles)
                .Include(t => t.TaskComments)
                    .ThenInclude(tc => tc.User)
                .Where(t => t.WorkspaceId == workspaceId)
                .ToListAsync();
        });

        // Cache Workspace Files
        var allFiles = await _cache.GetOrCreateAsync($"WorkspaceFiles_{workspaceId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _context.WorkspaceFiles
                .Include(wf => wf.User)
                .Where(wf => wf.WorkspaceId == workspaceId)
                .OrderByDescending(wf => wf.CreatedAt)
                .ToListAsync();
        });

        Files = allFiles;

        // Cache Chat Room & Messages
        ChatRoom = await _cache.GetOrCreateAsync($"WorkspaceChatRoom_{workspaceId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _context.ChatRooms.FirstOrDefaultAsync(cr => cr.WorkspaceId == workspaceId);
        });

        if (ChatRoom != null)
        {
            ChatMessages = await _cache.GetOrCreateAsync($"WorkspaceChatMessages_{ChatRoom.Id}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.ChatMessages
                    .Include(cm => cm.Sender)
                    .Where(cm => cm.RoomId == ChatRoom.Id)
                    .OrderBy(cm => cm.SentAt)
                    .ToListAsync();
            });
        }

        string packageTier = Workspace.PackageTier ?? "Free";
        ShowVisibilityControls = (packageTier == "Personal" && Members.Count >= 2);

        return true;
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostCreateTaskAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");
        
        bool canCreate = IsMemberAllowed(CurrentUser.Id, "disabledCreateTaskUsers", CurrentUserRole);
        if (!canCreate) return Forbid();

        if (!string.IsNullOrEmpty(NewTaskTitle))
        {
            NewTaskTitle = Helpers.InputSanitizer.SanitizeInput(NewTaskTitle);
            NewTaskDescription = Helpers.InputSanitizer.SanitizeInput(NewTaskDescription);

            // Default time component to 23:50 (11:50 PM) if time was omitted (Midnight 12:00 AM)
            if (NewTaskDueDate.HasValue && NewTaskDueDate.Value.Hour == 0 && NewTaskDueDate.Value.Minute == 0 && NewTaskDueDate.Value.Second == 0)
            {
                NewTaskDueDate = NewTaskDueDate.Value.Date.AddHours(23).AddMinutes(50);
            }

            var task = new unigrid.Models.Task
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Workspace.Id,
                AssigneeId = NewTaskAssigneeId,
                Title = NewTaskTitle,
                Description = NewTaskDescription,
                Status = NewTaskStatus,
                Priority = NewTaskPriority,
                DueDate = NewTaskDueDate,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Tasks.AddAsync(task);

            if (task.AssigneeId.HasValue && task.AssigneeId.Value != CurrentUser.Id)
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = task.AssigneeId.Value,
                    Message = $"You have been assigned the task '{task.Title}' in Workspace '{Workspace.Name}' by {CurrentUser.FullName}.",
                    Type = "TaskAssignment",
                    Link = $"/WorkspaceDetail/{Workspace.JoinCode}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    RelatedId = task.Id
                };
                await _context.Notifications.AddAsync(notification);
            }

            await _context.SaveChangesAsync();

            // Handle file attachment if any
            if (NewTaskFile != null && NewTaskFile.Length > 0)
            {
                var file = await ProcessFileUploadAsync(NewTaskFile, task.Id);
                if (file != null)
                {
                    await _context.WorkspaceFiles.AddAsync(file);
                    await _context.SaveChangesAsync();
                }
            }

            EvictAllWorkspaceMembersCache(Workspace.Id);
            _logger.LogInformation("Task created: {Title} in Workspace {WorkspaceId}", NewTaskTitle, Workspace.Id);
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateTaskStatusAsync(string joinCode, Guid taskId, int status)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");
        if (CurrentUserRole == "Viewer") return Forbid();

        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.WorkspaceId == Workspace.Id);
        if (task != null)
        {
            // Backend Permission Check
            if (CurrentUserRole != "Manager" && CurrentUserRole != "Vice Manager")
            {
                if (task.AssigneeId != CurrentUser.Id)
                {
                    return Forbid(); // Members can only move their own assigned tasks
                }
                if (status == 3)
                {
                    return Forbid(); // Normal members cannot move tasks directly to Done
                }
                if (task.Status == 3)
                {
                    return Forbid(); // Normal members cannot move completed tasks out of Done (rework)
                }
            }

            int oldStatus = task.Status ?? 0;
            task.Status = status;

            if (task.AssigneeId.HasValue && task.AssigneeId.Value != CurrentUser.Id)
            {
                string msg = "";
                if (status == 3) // Done (Approved)
                {
                    msg = $"Your task '{task.Title}' in Workspace '{Workspace.Name}' has been APPROVED by {CurrentUser.FullName}.";
                }
                else if ((oldStatus == 2 || oldStatus == 3) && (status == 0 || status == 1)) // Back to Todo/In Progress (Rework)
                {
                    msg = $"Your task '{task.Title}' in Workspace '{Workspace.Name}' has been requested for REWORK by {CurrentUser.FullName}.";
                }

                if (!string.IsNullOrEmpty(msg))
                {
                    var notification = new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = task.AssigneeId.Value,
                        Message = msg,
                        Type = status == 3 ? "TaskApproved" : "TaskRework",
                        Link = $"/WorkspaceDetail/{Workspace.JoinCode}",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow,
                        RelatedId = task.Id
                    };
                    await _context.Notifications.AddAsync(notification);
                }
            }

            await _context.SaveChangesAsync();
            EvictAllWorkspaceMembersCache(Workspace.Id);
            _logger.LogInformation("Task status updated. TaskId: {TaskId}, NewStatus: {Status}", taskId, status);
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostEditTaskAsync(string joinCode, Guid editTaskId, string editTaskTitle, string editTaskDescription, int editTaskPriority, Guid? editTaskAssigneeId, DateTime? editTaskDueDate)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");
        
        bool canEdit = IsMemberAllowed(CurrentUser.Id, "disabledEditTaskUsers", CurrentUserRole);
        if (!canEdit) return Forbid();

        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == editTaskId && t.WorkspaceId == Workspace.Id);
        if (task != null)
        {
            Guid? oldAssigneeId = task.AssigneeId;
            // Backend Permission Check
            // Members can only edit description, while Managers/Vice Managers can edit everything
            if (CurrentUserRole == "Manager" || CurrentUserRole == "Vice Manager")
            {
                task.Title = Helpers.InputSanitizer.SanitizeInput(editTaskTitle);
                task.Priority = editTaskPriority;
                task.AssigneeId = editTaskAssigneeId;

                // Default time component to 23:50 (11:50 PM) if time was omitted (Midnight 12:00 AM)
                if (editTaskDueDate.HasValue && editTaskDueDate.Value.Hour == 0 && editTaskDueDate.Value.Minute == 0 && editTaskDueDate.Value.Second == 0)
                {
                    editTaskDueDate = editTaskDueDate.Value.Date.AddHours(23).AddMinutes(50);
                }
                task.DueDate = editTaskDueDate;
            }
            
            task.Description = Helpers.InputSanitizer.SanitizeInput(editTaskDescription);

            // Handle file attachment if any
            if (EditTaskFile != null && EditTaskFile.Length > 0)
            {
                var file = await ProcessFileUploadAsync(EditTaskFile, task.Id);
                if (file != null)
                {
                    await _context.WorkspaceFiles.AddAsync(file);
                }
            }

            if (editTaskAssigneeId.HasValue && editTaskAssigneeId != oldAssigneeId && editTaskAssigneeId.Value != CurrentUser.Id)
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = editTaskAssigneeId.Value,
                    Message = $"You have been assigned the task '{task.Title}' in Workspace '{Workspace.Name}' by {CurrentUser.FullName}.",
                    Type = "TaskAssignment",
                    Link = $"/WorkspaceDetail/{Workspace.JoinCode}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    RelatedId = task.Id
                };
                await _context.Notifications.AddAsync(notification);
            }

            await _context.SaveChangesAsync();
            EvictAllWorkspaceMembersCache(Workspace.Id);
            _logger.LogInformation("Task edited. TaskId: {TaskId}", editTaskId);
        }

        return RedirectToPage(new { joinCode });
    }

    private async System.Threading.Tasks.Task<WorkspaceFile?> ProcessFileUploadAsync(Microsoft.AspNetCore.Http.IFormFile uploadedFile, Guid taskId)
    {
        if (uploadedFile == null || uploadedFile.Length == 0) return null;

        string originalFileName = uploadedFile.FileName;
        string baseName = System.IO.Path.GetFileNameWithoutExtension(originalFileName);
        string extension = System.IO.Path.GetExtension(originalFileName).TrimStart('.').ToLower();

        if (string.IsNullOrEmpty(baseName) || baseName.Contains('.')) return null;

        foreach (char c in baseName)
        {
            if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_') return null;
        }

        string fileType = "doc";
        if (extension == "pdf") fileType = "pdf";
        else if (extension == "xls" || extension == "xlsx" || extension == "csv") fileType = "spreadsheet";
        else if (extension == "png" || extension == "jpg" || extension == "jpeg" || extension == "gif" || extension == "svg") fileType = "image";

        long maxStorageLimit = 0;
        string packageTier = Workspace.PackageTier ?? "Free";
        bool isIndividualStorage = (packageTier == "Personal");

        if (isIndividualStorage)
        {
            // Individual storage calculation for Personal plan workspace
            string userTier = CurrentUser.SubscriptionTier ?? "Free";
            if (userTier == "Personal") maxStorageLimit = 2L * 1024 * 1024 * 1024; // 2 GB
            else if (userTier == "Pro") maxStorageLimit = 20L * 1024 * 1024 * 1024; // 20 GB
            else if (userTier == "ProPlus") maxStorageLimit = 40L * 1024 * 1024 * 1024; // 40 GB
            else if (userTier == "Business") maxStorageLimit = 80L * 1024 * 1024 * 1024; // 80 GB
            else maxStorageLimit = 0; // Free user gets 0 GB
        }
        else
        {
            // Workspace-wide shared storage calculation for other plans
            if (packageTier == "Pro") maxStorageLimit = 20L * 1024 * 1024 * 1024;
            else if (packageTier == "ProPlus") maxStorageLimit = 40L * 1024 * 1024 * 1024;
            else if (packageTier == "Business") maxStorageLimit = 80L * 1024 * 1024 * 1024;
        }

        if (packageTier == "Free") return null;
        if (isIndividualStorage && maxStorageLimit <= 0) return null;

        long totalStorageUsed = 0;
        if (isIndividualStorage)
        {
            totalStorageUsed = await _context.WorkspaceFiles
                .Where(f => f.WorkspaceId == Workspace.Id && f.UserId == CurrentUser.Id)
                .SumAsync(f => f.FileSize);
        }
        else
        {
            totalStorageUsed = await _context.WorkspaceFiles
                .Where(f => f.WorkspaceId == Workspace.Id)
                .SumAsync(f => f.FileSize);
        }

        if (totalStorageUsed + uploadedFile.Length > maxStorageLimit) return null;

        string uploadDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "files", Workspace.Id.ToString());
        if (!System.IO.Directory.Exists(uploadDir))
        {
            System.IO.Directory.CreateDirectory(uploadDir);
        }

        string safeFileName = originalFileName.ToLower().Replace(" ", "_");
        string physicalPath = System.IO.Path.Combine(uploadDir, safeFileName);

        using (var stream = new System.IO.FileStream(physicalPath, System.IO.FileMode.Create))
        {
            await uploadedFile.CopyToAsync(stream);
        }

        return new WorkspaceFile
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Workspace.Id,
            TaskId = taskId,
            UserId = CurrentUser.Id,
            FileName = originalFileName,
            FileUrl = $"files/{Workspace.Id}/{safeFileName}",
            FileType = fileType,
            FileSize = uploadedFile.Length,
            IsPublic = true,
            CreatedAt = DateTime.UtcNow
        };
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

        if (CurrentUserRole == "Viewer")
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Viewer role cannot add comments." });
            }
            return Forbid();
        }

        if (!string.IsNullOrEmpty(CommentContent) && CommentTaskId != Guid.Empty)
        {
            CommentContent = Helpers.InputSanitizer.SanitizeInput(CommentContent);
            var comment = new TaskComment
            {
                Id = Guid.NewGuid(),
                TaskId = CommentTaskId,
                UserId = CurrentUser.Id,
                Content = CommentContent,
                CreatedAt = DateTime.UtcNow
            };

            await _context.TaskComments.AddAsync(comment);
            await _context.SaveChangesAsync();
            _cache.Remove($"WorkspaceTasks_{Workspace.Id}");
            _logger.LogInformation("Comment added to task {TaskId} by {UserId}", CommentTaskId, CurrentUser.Id);

            var payload = new
            {
                id = comment.Id,
                taskId = comment.TaskId,
                userId = comment.UserId,
                userName = CurrentUser.FullName,
                content = comment.Content,
                createdAt = comment.CreatedAt
            };

            await _hubContext.Clients.Group(Workspace.Id.ToString()).SendAsync("ReceiveTaskComment", payload);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new JsonResult(payload);
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

        if (CurrentUserRole == "Viewer")
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Viewer role cannot send chat messages." });
            }
            return Forbid();
        }

        if (!string.IsNullOrEmpty(ChatContent) && ChatRoom != null)
        {
            if (ChatContent.StartsWith("[system:channel_rules]"))
            {
                try
                {
                    string jsonStr = ChatContent.Substring("[system:channel_rules]".Length);
                    var incomingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(jsonStr);
                    if (incomingPayload != null && incomingPayload["allChannels"] != null)
                    {
                        var incomingChannels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(incomingPayload["allChannels"].ToJsonString()) ?? new List<string>();
                        
                        var existingChannels = new List<string> { "general" };
                        string? currentSettings = Workspace.SettingsJson;
                        if (!string.IsNullOrEmpty(currentSettings))
                        {
                            try
                            {
                                var existingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(currentSettings);
                                if (existingPayload != null && existingPayload["allChannels"] != null)
                                {
                                    existingChannels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(existingPayload["allChannels"].ToJsonString()) ?? existingChannels;
                                }
                            }
                            catch {}
                        }

                        bool isAddingChannel = incomingChannels.Any(c => !existingChannels.Contains(c));
                        if (isAddingChannel)
                        {
                            if (!IsMemberAllowedToCreateChannel(CurrentUser.Id))
                            {
                                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                                {
                                    return new BadRequestObjectResult(new { message = "You do not have permission to create chat channels." });
                                }
                                TempData["ErrorMessage"] = "You do not have permission to create chat channels.";
                                return RedirectToPage(new { joinCode });
                            }
                        }
                        else
                        {
                            bool isManagerOrOwner = CurrentUserRole == "Manager" || Workspace.OwnerId == CurrentUser.Id;
                            if (!isManagerOrOwner)
                            {
                                string ch = activeChannel ?? "general";
                                if (ch != "general")
                                {
                                    var channelOwners = new Dictionary<string, string>();
                                    var channelModerators = new Dictionary<string, List<string>>();
                                    
                                    if (!string.IsNullOrEmpty(currentSettings))
                                    {
                                        try
                                        {
                                            var existingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(currentSettings);
                                            if (existingPayload != null)
                                            {
                                                if (existingPayload["channelOwners"] != null)
                                                    channelOwners = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(existingPayload["channelOwners"].ToJsonString()) ?? channelOwners;
                                                if (existingPayload["channelModerators"] != null)
                                                    channelModerators = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(existingPayload["channelModerators"].ToJsonString()) ?? channelModerators;
                                            }
                                        }
                                        catch {}
                                    }

                                    bool isChannelOwner = channelOwners.TryGetValue(ch, out var oId) && oId.ToLower() == CurrentUser.Id.ToString().ToLower();
                                    bool isChannelMod = channelModerators.TryGetValue(ch, out var mods) && mods.Any(m => m.ToLower() == CurrentUser.Id.ToString().ToLower());
                                    
                                    if (!isChannelOwner && !isChannelMod)
                                    {
                                        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                                        {
                                            return new BadRequestObjectResult(new { message = "You do not have permission to manage this channel's access rules." });
                                        }
                                        TempData["ErrorMessage"] = "You do not have permission to manage this channel's access rules.";
                                        return RedirectToPage(new { joinCode });
                                    }

                                    // Robust self-modification check for Channel Moderators (who are not Channel Owners or Workspace Owners/Managers)
                                    if (!isChannelOwner)
                                    {
                                        // 1. Verify owner of the channel did not change
                                        string existingOwner = channelOwners.TryGetValue(ch, out var eo) ? eo.ToLower() : "";
                                        
                                        var incomingOwners = new Dictionary<string, string>();
                                        if (incomingPayload["channelOwners"] != null)
                                        {
                                            incomingOwners = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(incomingPayload["channelOwners"].ToJsonString()) ?? incomingOwners;
                                        }
                                        string incomingOwner = incomingOwners.TryGetValue(ch, out var io) ? io.ToLower() : "";
                                        
                                        if (existingOwner != incomingOwner)
                                        {
                                            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                                            {
                                                return new BadRequestObjectResult(new { message = "Only the channel owner or workspace owner/manager can transfer channel ownership." });
                                            }
                                            TempData["ErrorMessage"] = "Only the channel owner or workspace owner/manager can transfer channel ownership.";
                                            return RedirectToPage(new { joinCode });
                                        }

                                        // 2. Verify their own Access did not change
                                        var existingLocked = new Dictionary<string, List<string>>();
                                        if (!string.IsNullOrEmpty(currentSettings))
                                        {
                                            try
                                            {
                                                var existingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(currentSettings);
                                                if (existingPayload != null && existingPayload["lockedChannels"] != null)
                                                {
                                                    existingLocked = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(existingPayload["lockedChannels"].ToJsonString()) ?? existingLocked;
                                                }
                                            }
                                            catch {}
                                        }
                                        
                                        var incomingLocked = new Dictionary<string, List<string>>();
                                        if (incomingPayload["lockedChannels"] != null)
                                        {
                                            incomingLocked = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(incomingPayload["lockedChannels"].ToJsonString()) ?? incomingLocked;
                                        }

                                        var existingAccessList = existingLocked.TryGetValue(ch, out var elist) ? elist.Select(id => id.ToLower()).ToList() : new List<string>();
                                        var incomingAccessList = incomingLocked.TryGetValue(ch, out var ilist) ? ilist.Select(id => id.ToLower()).ToList() : new List<string>();

                                        string myId = CurrentUser.Id.ToString().ToLower();
                                        bool hadAccess = existingAccessList.Contains(myId);
                                        bool hasAccessNow = incomingAccessList.Contains(myId);

                                        if (hadAccess != hasAccessNow)
                                        {
                                            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                                            {
                                                return new BadRequestObjectResult(new { message = "You cannot modify your own channel access." });
                                            }
                                            TempData["ErrorMessage"] = "You cannot modify your own channel access.";
                                            return RedirectToPage(new { joinCode });
                                        }

                                        // 3. Verify their own Mod status did not change
                                        var incomingModsDict = new Dictionary<string, List<string>>();
                                        if (incomingPayload["channelModerators"] != null)
                                        {
                                            incomingModsDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(incomingPayload["channelModerators"].ToJsonString()) ?? incomingModsDict;
                                        }
                                        
                                        var existingModsList = channelModerators.TryGetValue(ch, out var emods) ? emods.Select(id => id.ToLower()).ToList() : new List<string>();
                                        var incomingModsList = incomingModsDict.TryGetValue(ch, out var imods) ? imods.Select(id => id.ToLower()).ToList() : new List<string>();

                                        bool wasMod = existingModsList.Contains(myId);
                                        bool isModNow = incomingModsList.Contains(myId);

                                        if (wasMod != isModNow)
                                        {
                                            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                                            {
                                                return new BadRequestObjectResult(new { message = "You cannot modify your own channel moderator status." });
                                            }
                                            TempData["ErrorMessage"] = "You cannot modify your own channel moderator status.";
                                            return RedirectToPage(new { joinCode });
                                        }

                                        // 4. Verify they did not edit moderators for other users (only Channel Owners or Workspace Managers can add/remove other moderators)
                                        var otherExistingMods = existingModsList.Where(id => id != myId).OrderBy(id => id).ToList();
                                        var otherIncomingMods = incomingModsList.Where(id => id != myId).OrderBy(id => id).ToList();
                                        if (!otherExistingMods.SequenceEqual(otherIncomingMods))
                                        {
                                            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                                            {
                                                return new BadRequestObjectResult(new { message = "Only the channel owner or workspace owner/manager can modify moderator roles." });
                                            }
                                            TempData["ErrorMessage"] = "Only the channel owner or workspace owner/manager can modify moderator roles.";
                                            return RedirectToPage(new { joinCode });
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Save the system channel rules directly to Workspace settings (query from DB to avoid EF Core tracking graph conflicts)
                    var dbWorkspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == Workspace.Id);
                    if (dbWorkspace != null)
                    {
                        dbWorkspace.SettingsJson = jsonStr;
                    }
                    await _context.SaveChangesAsync();

                    // Evict Cache
                    _cache.Remove($"Workspace_{joinCode}");

                    // Broadcast real-time SignalR rules update payload to all active clients
                    var broadcastPayload = new
                    {
                        id = Guid.NewGuid(),
                        roomId = ChatRoom.Id,
                        senderId = CurrentUser.Id,
                        senderName = CurrentUser.FullName,
                        content = "[system:channel_rules]",
                        rawContent = ChatContent,
                        sentAt = DateTime.UtcNow,
                        channel = "general"
                    };
                    await _hubContext.Clients.Group(Workspace.Id.ToString()).SendAsync("ReceiveChatMessage", broadcastPayload);

                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return new JsonResult(new { success = true });
                    }
                    return RedirectToPage(new { joinCode });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to validate incoming system rules payload");
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return new BadRequestObjectResult(new { message = "Invalid channel rules payload." });
                    }
                    return RedirectToPage(new { joinCode });
                }
            }

            if (!ChatContent.StartsWith("[system:"))
            {
                ChatContent = Helpers.InputSanitizer.SanitizeInput(ChatContent);
            }
            string contentWithChannel = ChatContent;
            if (!string.IsNullOrEmpty(activeChannel) && activeChannel != "general")
            {
                contentWithChannel = $"[channel:{activeChannel}]{ChatContent}";
            }

            if (selectedFileId.HasValue)
            {
                contentWithChannel = $"[file:{selectedFileId.Value}]{contentWithChannel}";
            }

            var message = new ChatMessage
            {
                Id = Guid.NewGuid(),
                RoomId = ChatRoom.Id,
                SenderId = CurrentUser.Id,
                Content = contentWithChannel,
                SentAt = DateTime.UtcNow,
                IsDeleted = false
            };

            await _context.ChatMessages.AddAsync(message);
            await _context.SaveChangesAsync();
            _cache.Remove($"WorkspaceChatMessages_{ChatRoom.Id}");
            _logger.LogInformation("Chat message sent in room {RoomId} in channel {Channel} by {UserId}", ChatRoom.Id, activeChannel, CurrentUser.Id);

            string cleanContent = ChatContent;
            var payload = new
            {
                id = message.Id,
                roomId = message.RoomId,
                senderId = message.SenderId,
                senderName = CurrentUser.FullName,
                content = cleanContent,
                rawContent = message.Content,
                sentAt = message.SentAt,
                channel = string.IsNullOrEmpty(activeChannel) ? "general" : activeChannel
            };

            await _hubContext.Clients.Group(Workspace.Id.ToString()).SendAsync("ReceiveChatMessage", payload);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new JsonResult(payload);
            }
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
        if (CurrentUserRole == "Viewer")
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Viewer role cannot upload files." });
            }
            return Forbid();
        }

        if (UploadedFile == null || UploadedFile.Length == 0)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "No file was selected for upload." });
            }
            TempData["UploadError"] = "No file was selected for upload.";
            return RedirectToPage(new { joinCode });
        }

        string originalFileName = UploadedFile.FileName;
        string baseName = System.IO.Path.GetFileNameWithoutExtension(originalFileName);
        string extension = System.IO.Path.GetExtension(originalFileName).TrimStart('.').ToLower();

        // Validate filename: Not allow regex or . characters in base filename
        if (string.IsNullOrEmpty(baseName))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Invalid file name." });
            }
            TempData["UploadError"] = "Invalid file name.";
            return RedirectToPage(new { joinCode });
        }

        // Check for any dot in the base name
        if (baseName.Contains('.'))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "File name contains invalid characters. Multiple dots ('.') are not allowed in the file name." });
            }
            TempData["UploadError"] = "File name contains invalid characters. Multiple dots ('.') are not allowed in the file name.";
            return RedirectToPage(new { joinCode });
        }

        // Check for special/regex characters (allow only letters, digits, spaces, hyphens, underscores)
        foreach (char c in baseName)
        {
            if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return new BadRequestObjectResult(new { message = "File name contains invalid characters. Only alphanumeric characters, spaces, hyphens, and underscores are allowed." });
                }
                TempData["UploadError"] = "File name contains invalid characters. Only alphanumeric characters, spaces, hyphens, and underscores are allowed.";
                return RedirectToPage(new { joinCode });
            }
        }

        // Map extension to our app's predefined categories
        string fileType = "doc";
        if (extension == "pdf")
        {
            fileType = "pdf";
        }
        else if (extension == "xls" || extension == "xlsx" || extension == "csv")
        {
            fileType = "spreadsheet";
        }
        else if (extension == "png" || extension == "jpg" || extension == "jpeg" || extension == "gif" || extension == "svg")
        {
            fileType = "image";
        }

        // 1. Get workspace plan and maximum allowed storage limit
        long maxStorageLimit = 0;
        string packageTier = Workspace.PackageTier ?? "Free";
        bool isIndividualStorage = (packageTier == "Personal");

        if (isIndividualStorage)
        {
            // Individual storage calculation for Personal plan workspace
            string userTier = CurrentUser.SubscriptionTier ?? "Free";
            if (userTier == "Personal") maxStorageLimit = 2L * 1024 * 1024 * 1024; // 2 GB
            else if (userTier == "Pro") maxStorageLimit = 20L * 1024 * 1024 * 1024; // 20 GB
            else if (userTier == "ProPlus") maxStorageLimit = 40L * 1024 * 1024 * 1024; // 40 GB
            else if (userTier == "Business") maxStorageLimit = 80L * 1024 * 1024 * 1024; // 80 GB
            else maxStorageLimit = 0; // Free user gets 0 GB
        }
        else
        {
            // Workspace-wide shared storage calculation for other plans
            if (packageTier == "Pro") maxStorageLimit = 20L * 1024 * 1024 * 1024;
            else if (packageTier == "ProPlus") maxStorageLimit = 40L * 1024 * 1024 * 1024;
            else if (packageTier == "Business") maxStorageLimit = 80L * 1024 * 1024 * 1024;
        }

        // 2. Perform limit validation
        if (packageTier == "Free")
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "File uploads are not allowed on the Free plan. Please upgrade your workspace package to upload files." });
            }
            TempData["UploadError"] = "File uploads are not allowed on the Free plan. Please upgrade your workspace package to upload files.";
            return RedirectToPage(new { joinCode });
        }

        if (isIndividualStorage && maxStorageLimit <= 0)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "You do not have individual storage upload privileges in this workspace. Upgrade to a Personal plan to upload files." });
            }
            TempData["UploadError"] = "You do not have individual storage upload privileges in this workspace. Upgrade to a Personal plan to upload files.";
            return RedirectToPage(new { joinCode });
        }

        // 3. Sum up existing file sizes in the database
        long totalStorageUsed = 0;
        if (isIndividualStorage)
        {
            totalStorageUsed = await _context.WorkspaceFiles
                .Where(f => f.WorkspaceId == Workspace.Id && f.UserId == CurrentUser.Id)
                .SumAsync(f => f.FileSize);
        }
        else
        {
            totalStorageUsed = await _context.WorkspaceFiles
                .Where(f => f.WorkspaceId == Workspace.Id)
                .SumAsync(f => f.FileSize);
        }

        if (totalStorageUsed + UploadedFile.Length > maxStorageLimit)
        {
            string limitStr = maxStorageLimit >= 1024L * 1024 * 1024 
                ? $"{(maxStorageLimit / (1024L * 1024 * 1024))} GB" 
                : "0 GB";
            string typeStr = isIndividualStorage ? "individual" : "workspace";
            string errorMsg = $"Upload failed. You have exceeded your {typeStr} storage limit of {limitStr} for the {packageTier} plan.";
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = errorMsg });
            }
            TempData["UploadError"] = errorMsg;
            return RedirectToPage(new { joinCode });
        }

        // 4. Save file uniquely and isolate it under the workspace directory
        string uploadDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "files", Workspace.Id.ToString());
        if (!System.IO.Directory.Exists(uploadDir))
        {
            System.IO.Directory.CreateDirectory(uploadDir);
        }

        string safeFileName = originalFileName.ToLower().Replace(" ", "_");
        string physicalPath = System.IO.Path.Combine(uploadDir, safeFileName);

        // Copy file to local storage path
        using (var stream = new System.IO.FileStream(physicalPath, System.IO.FileMode.Create))
        {
            await UploadedFile.CopyToAsync(stream);
        }

        // 5. Create database record with isolated file URL
        var file = new WorkspaceFile
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Workspace.Id,
            UserId = CurrentUser.Id,
            FileName = originalFileName,
            FileUrl = $"files/{Workspace.Id}/{safeFileName}",
            FileType = fileType,
            FileSize = UploadedFile.Length,
            IsPublic = true,
            CreatedAt = DateTime.UtcNow
        };

        await _context.WorkspaceFiles.AddAsync(file);
        await _context.SaveChangesAsync();
        EvictAllWorkspaceMembersCache(Workspace.Id);
        
        _logger.LogInformation("File uploaded in workspace {WorkspaceId} by {UserId}", Workspace.Id, CurrentUser.Id);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return new JsonResult(new {
                success = true,
                file = new {
                    id = file.Id.ToString(),
                    fileName = file.FileName,
                    fileUrl = file.FileUrl,
                    fileSize = file.FileSize,
                    fileType = file.FileType
                }
            });
        }

        TempData["UploadSuccess"] = $"Successfully uploaded file: {originalFileName}";
        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostInviteMemberAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");
        if (CurrentUserRole != "Manager" && CurrentUserRole != "Vice Manager") return Forbid();

        if (Workspace.PackageTier == "Personal")
        {
            TempData["InviteError"] = "Cannot invite members in a Personal plan workspace.";
            return RedirectToPage(new { joinCode });
        }

        if (!string.IsNullOrEmpty(InviteEmail))
        {
            var email = InviteEmail.Trim();

            // Find user profile by email through Account relationship
            var inviteeUser = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.Account.Email.ToLower() == email.ToLower());

            if (inviteeUser != null)
            {
                var alreadyMember = await _context.WorkspaceMembers
                    .AnyAsync(wm => wm.WorkspaceId == Workspace.Id && wm.UserId == inviteeUser.Id);

                if (alreadyMember)
                {
                    TempData["InviteError"] = "This user is already a member of this workspace.";
                    return RedirectToPage(new { joinCode });
                }
            }

            // Check if there is already a pending invitation for this email
            var existingInvitation = await _context.WorkspaceInvitations
                .FirstOrDefaultAsync(i => i.WorkspaceId == Workspace.Id && i.InviteeEmail.ToLower() == email.ToLower() && i.Status == "Pending");

            if (existingInvitation != null)
            {
                TempData["InviteError"] = "An invitation has already been sent to this email and is pending confirmation.";
                return RedirectToPage(new { joinCode });
            }

            // Enforce plan-based maximum member limit checks (counting both active members and pending invites)
            int currentMembersCount = await _context.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == Workspace.Id);
            int pendingInvitesCount = await _context.WorkspaceInvitations.CountAsync(i => i.WorkspaceId == Workspace.Id && i.Status == "Pending");
            
            int maxMembersAllowed = 5; // Default for Free and Personal
            string tier = Workspace.PackageTier ?? "Free";
            if (tier == "Pro") maxMembersAllowed = 10;
            else if (tier == "ProPlus") maxMembersAllowed = 15;
            else if (tier == "Business") maxMembersAllowed = 30;

            if (currentMembersCount + pendingInvitesCount >= maxMembersAllowed)
            {
                TempData["InviteError"] = $"Cannot send invitation. The workspace has reached the member limit ({maxMembersAllowed}) of the {tier} plan.";
                return RedirectToPage(new { joinCode });
            }

            // Create WorkspaceInvitation
            var invitation = new WorkspaceInvitation
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Workspace.Id,
                InviterId = CurrentUser.Id,
                InviteeEmail = email,
                Role = InviteRole,
                DisplayRole = Helpers.InputSanitizer.SanitizeInput(InviteDisplayRole),
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            await _context.WorkspaceInvitations.AddAsync(invitation);

            // If the user already has an account, send a notification immediately!
            if (inviteeUser != null)
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = inviteeUser.Id,
                    Message = $"{CurrentUser.FullName} has invited you to join Workspace '{Workspace.Name}' as a '{InviteRole}'.",
                    Type = "WorkspaceInvitation",
                    Link = $"/api/invitations/{invitation.Id}/accept",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    RelatedId = Workspace.Id
                };
                await _context.Notifications.AddAsync(notification);
            }

            await _context.SaveChangesAsync();
            TempData["InviteSuccess"] = $"Invitation successfully sent to {email}!";
            _logger.LogInformation("Invitation created for email {Email} as {Role} in Workspace {WorkspaceId}", email, InviteRole, Workspace.Id);
        }

        return RedirectToPage(new { joinCode });
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
            
            // Check for potential nested file prefix in remainder content
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

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateMemberRoleAsync(string joinCode, Guid memberId, string newRole, string newDisplayRole, bool canDeleteFile, bool canCreateTask, bool canEditTask, bool canCreateChannel, bool canDeleteTask)
    {
        var result = await LoadWorkspaceDataAsync(joinCode);
        if (!result)
        {
            return RedirectToPage("/Dashboard");
        }

        var memberToUpdate = await _context.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == Workspace.Id && m.UserId == memberId);

        if (memberToUpdate == null)
        {
            TempData["ErrorMessage"] = "Member does not exist in this workspace.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        // PERSONAL DISPLAY ROLE EDITING:
        // Any member can update their OWN display role/title!
        if (memberId == CurrentUser.Id)
        {
            memberToUpdate.DisplayRole = Helpers.InputSanitizer.SanitizeInput(newDisplayRole);
            _context.WorkspaceMembers.Update(memberToUpdate);
            await _context.SaveChangesAsync();

            // Evict caches
            _cache.Remove($"Workspace_{joinCode}");
            _cache.Remove($"WorkspaceMembers_{Workspace.Id}");
            EvictAllWorkspaceMembersCache(Workspace.Id);

            TempData["SuccessMessage"] = "Successfully updated your display role.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        // Only Manager can modify roles of other members
        if (CurrentUserRole != "Manager")
        {
            TempData["ErrorMessage"] = "You do not have permission to perform this action. Only the Manager can modify roles.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        // Prevent changing own role (this is already covered by the OWN display role editing block above, but keeping it as safeguard)
        if (memberToUpdate.UserId == CurrentUser.Id)
        {
            TempData["ErrorMessage"] = "You cannot modify your own role.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        // ENFORCE SINGLE-MANAGER RULE
        if (newRole == "Manager")
        {
            TempData["ErrorMessage"] = "A workspace is only allowed to have a single Manager (the Owner). You can appoint this member as a Vice Manager instead.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        var validRoles = new List<string> { "Vice Manager", "Member", "Viewer" };
        if (!validRoles.Contains(newRole))
        {
            TempData["ErrorMessage"] = "Invalid role specified.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        memberToUpdate.Role = newRole;
        memberToUpdate.DisplayRole = Helpers.InputSanitizer.SanitizeInput(newDisplayRole);
        memberToUpdate.CanDeleteFile = canDeleteFile;
        memberToUpdate.CanCreateTask = canCreateTask;
        memberToUpdate.CanEditTask = canEditTask;
        _context.WorkspaceMembers.Update(memberToUpdate);
        await _context.SaveChangesAsync();

        // -------------------------------------------------------------
        // SCHEMA-LESS VIRTUAL PERMISSIONS MANAGEMENT
        // Save all granular member permissions virtually in Workspace.SettingsJson
        // -------------------------------------------------------------
        if (ChatRoom != null)
        {
            string? jsonStr = Workspace.SettingsJson;

            var disabledCreateChannel = new List<string>();
            var disabledCreateTask = new List<string>();
            var disabledEditTask = new List<string>();
            var disabledDeleteFile = new List<string>();
            var disabledDeleteTask = new List<string>();

            var lockedChannels = new Dictionary<string, List<string>>();
            var channelOwners = new Dictionary<string, string>();
            var channelModerators = new Dictionary<string, List<string>>();
            var allChannels = new List<string> { "general" };

            if (!string.IsNullOrEmpty(jsonStr))
            {
                try
                {
                    var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonStr);
                    if (jsonNode != null)
                    {
                        var lockedChannelsNode = jsonNode["lockedChannels"];
                        if (lockedChannelsNode != null)
                        {
                            lockedChannels = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(lockedChannelsNode.ToJsonString()) ?? lockedChannels;
                        }
                        var channelOwnersNode = jsonNode["channelOwners"];
                        if (channelOwnersNode != null)
                        {
                            channelOwners = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(channelOwnersNode.ToJsonString()) ?? channelOwners;
                        }
                        var channelModeratorsNode = jsonNode["channelModerators"];
                        if (channelModeratorsNode != null)
                        {
                            channelModerators = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(channelModeratorsNode.ToJsonString()) ?? channelModerators;
                        }
                        var allChannelsNode = jsonNode["allChannels"];
                        if (allChannelsNode != null)
                        {
                            allChannels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(allChannelsNode.ToJsonString()) ?? allChannels;
                        }
                        
                        // Parse all current virtual settings
                        disabledCreateChannel = ParseListFromJsonNode(jsonNode["disabledCreateChannelUsers"]);
                        disabledCreateTask = ParseListFromJsonNode(jsonNode["disabledCreateTaskUsers"]);
                        disabledEditTask = ParseListFromJsonNode(jsonNode["disabledEditTaskUsers"]);
                        disabledDeleteFile = ParseListFromJsonNode(jsonNode["disabledDeleteFileUsers"]);
                        disabledDeleteTask = ParseListFromJsonNode(jsonNode["disabledDeleteTaskUsers"]);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse system channel rules inside role update.");
                }
            }

            string targetUserGuidStr = memberId.ToString().ToLower();
            
            UpdateDisabledListHelper(disabledCreateChannel, targetUserGuidStr, canCreateChannel);
            UpdateDisabledListHelper(disabledCreateTask, targetUserGuidStr, canCreateTask);
            UpdateDisabledListHelper(disabledEditTask, targetUserGuidStr, canEditTask);
            UpdateDisabledListHelper(disabledDeleteFile, targetUserGuidStr, canDeleteFile);
            UpdateDisabledListHelper(disabledDeleteTask, targetUserGuidStr, canDeleteTask);

            var newPayload = new
            {
                lockedChannels = lockedChannels,
                channelOwners = channelOwners,
                channelModerators = channelModerators,
                allChannels = allChannels,
                disabledCreateChannelUsers = disabledCreateChannel,
                disabledCreateTaskUsers = disabledCreateTask,
                disabledEditTaskUsers = disabledEditTask,
                disabledDeleteFileUsers = disabledDeleteFile,
                disabledDeleteTaskUsers = disabledDeleteTask
            };

            var serializedPayload = System.Text.Json.JsonSerializer.Serialize(newPayload);

            // Save to database Workspace table (query from DB to avoid EF Core tracking graph conflicts)
            var dbWorkspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == Workspace.Id);
            if (dbWorkspace != null)
            {
                dbWorkspace.SettingsJson = serializedPayload;
            }
            await _context.SaveChangesAsync();

            // Broadcast real-time SignalR rules update payload to all active clients
            var broadcastPayload = new
            {
                id = Guid.NewGuid(),
                roomId = ChatRoom.Id,
                senderId = CurrentUser.Id,
                senderName = CurrentUser.FullName,
                content = "[system:channel_rules]",
                rawContent = "[system:channel_rules]" + serializedPayload,
                sentAt = DateTime.UtcNow,
                channel = "general"
            };
            await _hubContext.Clients.Group(Workspace.Id.ToString()).SendAsync("ReceiveChatMessage", broadcastPayload);
        }

        // Evict caches
        _cache.Remove($"Workspace_{joinCode}");
        _cache.Remove($"WorkspaceMembers_{Workspace.Id}");
        EvictAllWorkspaceMembersCache(Workspace.Id);

        TempData["SuccessMessage"] = "Member role and permissions updated successfully.";
        return RedirectToPage("/WorkspaceDetail", new { joinCode });
    }

    private List<string> ParseListFromJsonNode(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node == null) return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(node.ToJsonString()) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private void UpdateDisabledListHelper(List<string> list, string userGuidStr, bool allowed)
    {
        if (!allowed)
        {
            if (!list.Contains(userGuidStr))
            {
                list.Add(userGuidStr);
            }
        }
        else
        {
            list.Remove(userGuidStr);
        }
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostLeaveWorkspaceAsync(string joinCode)
    {
        var result = await LoadWorkspaceDataAsync(joinCode);
        if (!result)
        {
            return RedirectToPage("/Dashboard");
        }

        var workspaceId = Workspace.Id;
        var currentMember = await _context.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == CurrentUser.Id);

        if (currentMember == null)
        {
            TempData["ErrorMessage"] = "You are not a member of this workspace.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        // Check logic if user is a Manager (or the Creator / Owner)
        bool isWorkspaceOwner = Workspace.OwnerId == CurrentUser.Id;
        bool isManager = currentMember.Role == "Manager" || isWorkspaceOwner;

        var otherMembers = await _context.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId && m.UserId != CurrentUser.Id)
            .ToListAsync();

        if (isManager && otherMembers.Any())
        {
            // Automated Succession Logic:
            // 1. Look for a Vice Manager to promote to Manager
            var successor = otherMembers.FirstOrDefault(m => m.Role == "Vice Manager");
            if (successor == null)
            {
                // 2. If no Vice Manager, promote a random active member
                successor = otherMembers.FirstOrDefault();
            }

            if (successor != null)
            {
                // Promote successor to Manager
                successor.Role = "Manager";
                _context.WorkspaceMembers.Update(successor);

                // If leaving user is the database Workspace.OwnerId, transfer ownership (query from DB to avoid EF Core tracking graph conflicts)
                if (isWorkspaceOwner)
                {
                    var dbWorkspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == Workspace.Id);
                    if (dbWorkspace != null)
                    {
                        dbWorkspace.OwnerId = successor.UserId;
                    }
                }

                _logger.LogInformation($"Workspace Succession: User {CurrentUser.Id} is leaving. Promoted user {successor.UserId} to Manager.");
            }
        }

        // Remove leaving user's membership
        _context.WorkspaceMembers.Remove(currentMember);
        await _context.SaveChangesAsync();

        // Evict caches
        _cache.Remove($"Workspace_{joinCode}");
        _cache.Remove($"WorkspaceMembers_{workspaceId}");
        EvictAllWorkspaceMembersCache(workspaceId);
        
        // Symmetrically clear user workspaces cache keys
        _cache.Remove($"UserWorkspaces_{CurrentUser.Id}");

        TempData["SuccessMessage"] = $"You have successfully left the workspace '{Workspace.Name}'.";
        return RedirectToPage("/Workspaces");
    }

    private void EvictAllWorkspaceMembersCache(Guid workspaceId)
    {
        _cache.Remove($"WorkspaceTasks_{workspaceId}");
        _cache.Remove($"WorkspaceFiles_{workspaceId}");
        
        if (Members != null)
        {
            foreach (var member in Members)
            {
                _cache.Remove($"UserWorkspaces_{member.UserId}");
                _cache.Remove($"UserTasks_{member.UserId}");
            }
        }
        
        if (Workspace != null)
        {
            _cache.Remove($"UserWorkspaces_{Workspace.OwnerId}");
            _cache.Remove($"UserTasks_{Workspace.OwnerId}");
        }
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteFileAsync(string joinCode, Guid fileId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");
        
        bool canDelete = IsMemberAllowed(CurrentUser.Id, "disabledDeleteFileUsers", CurrentUserRole);
        if (!canDelete) return Forbid();

        var file = await _context.WorkspaceFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.WorkspaceId == Workspace.Id);
        if (file != null)
        {
            // Delete physically
            string physicalPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", file.FileUrl);
            if (System.IO.File.Exists(physicalPath))
            {
                try
                {
                    System.IO.File.Delete(physicalPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to physically delete file: {Path}", physicalPath);
                }
            }

            _context.WorkspaceFiles.Remove(file);
            await _context.SaveChangesAsync();
            EvictAllWorkspaceMembersCache(Workspace.Id);
            TempData["SuccessMessage"] = $"Successfully deleted file '{file.FileName}'.";
            _logger.LogInformation("File deleted: {FileName} from Workspace {WorkspaceId}", file.FileName, Workspace.Id);
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
        
        bool canDelete = IsMemberAllowed(CurrentUser.Id, "disabledDeleteTaskUsers", CurrentUserRole);
        if (!canDelete)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "You do not have permission to delete tasks." });
            }
            return Forbid();
        }

        var task = await _context.Tasks
            .Include(t => t.TaskComments)
            .Include(t => t.WorkspaceFiles)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.WorkspaceId == Workspace.Id);

        if (task != null)
        {
            // 1. Delete associated personal schedules to prevent foreign key errors
            var personalSchedules = await _context.PersonalSchedules
                .Where(ps => ps.TaskId == taskId)
                .ToListAsync();
            if (personalSchedules.Any())
            {
                _context.PersonalSchedules.RemoveRange(personalSchedules);
            }

            // 2. Delete associated task comments
            if (task.TaskComments.Any())
            {
                _context.TaskComments.RemoveRange(task.TaskComments);
            }

            // 3. Clear file bindings (do not delete the files, just set TaskId = null)
            foreach (var file in task.WorkspaceFiles)
            {
                file.TaskId = null;
                _context.WorkspaceFiles.Update(file);
            }

            _context.Tasks.Remove(task);
            await _context.SaveChangesAsync();

            // Evict caches
            _cache.Remove($"Workspace_{joinCode}");
            _cache.Remove($"WorkspaceTasks_{Workspace.Id}");

            // SignalR broadcast task deletion!
            var payload = new { taskId = taskId };
            await _hubContext.Clients.Group(Workspace.Id.ToString()).SendAsync("ReceiveTaskDeletion", payload);

            TempData["SuccessMessage"] = $"Successfully deleted task '{task.Title}'.";
            _logger.LogInformation("Task deleted: {TaskId} from Workspace {WorkspaceId}", taskId, Workspace.Id);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new JsonResult(new { success = true, taskId = taskId });
            }
        }

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

        var message = await _context.ChatMessages
            .Include(m => m.Sender)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.RoomId == ChatRoom.Id);

        if (message == null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "Message not found." });
            }
            return RedirectToPage(new { joinCode });
        }

        // Authorization:
        // User must be the message sender OR Workspace Manager/Owner OR Channel Owner/Moderator of that message channel!
        bool isSender = message.SenderId == CurrentUser.Id;
        bool isManagerOrOwner = CurrentUserRole == "Manager" || Workspace.OwnerId == CurrentUser.Id;
        bool isAuthorized = isSender || isManagerOrOwner;

        if (!isAuthorized)
        {
            // Check channel ownership/moderators from latest rules message
            string channelName = "general";
            string cleanContent = message.Content;
            if (message.Content.StartsWith("[channel:"))
            {
                var endIndex = message.Content.IndexOf("]");
                if (endIndex > 9)
                {
                    channelName = message.Content.Substring(9, endIndex - 9);
                }
            }

            if (channelName != "general")
            {
                var messages = await _context.ChatMessages
                    .Where(cm => cm.RoomId == ChatRoom.Id)
                    .OrderBy(cm => cm.SentAt)
                    .ToListAsync();

                var latestRulesMsg = messages
                    .LastOrDefault(m => m.Content != null && m.Content.StartsWith("[system:channel_rules]"));

                var channelOwners = new Dictionary<string, string>();
                var channelModerators = new Dictionary<string, List<string>>();

                if (latestRulesMsg != null)
                {
                    try
                    {
                        string jsonStr = latestRulesMsg.Content.Substring("[system:channel_rules]".Length);
                        var existingPayload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(jsonStr);
                        if (existingPayload != null)
                        {
                            if (existingPayload["channelOwners"] != null)
                                channelOwners = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(existingPayload["channelOwners"].ToJsonString()) ?? channelOwners;
                            if (existingPayload["channelModerators"] != null)
                                channelModerators = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(existingPayload["channelModerators"].ToJsonString()) ?? channelModerators;
                        }
                    }
                    catch {}
                }

                bool isChannelOwner = channelOwners.TryGetValue(channelName, out var oId) && oId.ToLower() == CurrentUser.Id.ToString().ToLower();
                bool isChannelMod = channelModerators.TryGetValue(channelName, out var mods) && mods.Any(m => m.ToLower() == CurrentUser.Id.ToString().ToLower());
                
                isAuthorized = isChannelOwner || isChannelMod;
            }
        }

        if (!isAuthorized)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new BadRequestObjectResult(new { message = "You do not have permission to revoke this message." });
            }
            return Forbid();
        }

        // Soft delete: mark message as deleted
        message.IsDeleted = true;
        message.Content = "[deleted_message]" + message.Content; // Append prefix to know it was deleted
        _context.ChatMessages.Update(message);
        await _context.SaveChangesAsync();

        _cache.Remove($"WorkspaceChatMessages_{ChatRoom.Id}");

        // Broadcast message deletion via SignalR!
        var payload = new { messageId = messageId };
        await _hubContext.Clients.Group(Workspace.Id.ToString()).SendAsync("ReceiveMessageDeletion", payload);

        _logger.LogInformation("Chat message revoked: {MessageId} in Workspace {WorkspaceId} by {UserId}", messageId, Workspace.Id, CurrentUser.Id);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return new JsonResult(new { success = true, messageId = messageId });
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostTransferOwnershipAsync(string joinCode, Guid newOwnerId)
    {
        var result = await LoadWorkspaceDataAsync(joinCode);
        if (!result)
        {
            return RedirectToPage("/Dashboard");
        }

        // Only the absolute Owner can transfer ownership
        if (Workspace.OwnerId != CurrentUser.Id)
        {
            TempData["ErrorMessage"] = "Only the workspace owner can transfer ownership.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        var targetMember = await _context.WorkspaceMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.WorkspaceId == Workspace.Id && m.UserId == newOwnerId);

        if (targetMember == null)
        {
            TempData["ErrorMessage"] = "Selected member does not exist in this workspace.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        // Ensure we don't transfer to ourselves
        if (newOwnerId == CurrentUser.Id)
        {
            TempData["ErrorMessage"] = "You are already the workspace owner.";
            return RedirectToPage("/WorkspaceDetail", new { joinCode });
        }

        // 1. Get the current owner's membership record (if exists) or create one
        var currentOwnerMember = await _context.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == Workspace.Id && m.UserId == CurrentUser.Id);

        if (currentOwnerMember != null)
        {
            // Demote the current owner to Vice Manager
            currentOwnerMember.Role = "Vice Manager";
        }

        // 2. Promote the target member to Manager
        targetMember.Role = "Manager";
        targetMember.CanCreateTask = true;
        targetMember.CanEditTask = true;
        targetMember.CanDeleteFile = true;

        // 3. Update Workspace OwnerId in the database (query from DB to avoid EF Core tracking graph conflicts)
        var dbWorkspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == Workspace.Id);
        if (dbWorkspace != null)
        {
            dbWorkspace.OwnerId = newOwnerId;
        }

        await _context.SaveChangesAsync();

        // Evict caches
        _cache.Remove($"Workspace_{joinCode}");
        _cache.Remove($"WorkspaceMembers_{Workspace.Id}");
        EvictAllWorkspaceMembersCache(Workspace.Id);
        
        // Clear workspaces cache keys for both users
        _cache.Remove($"UserWorkspaces_{CurrentUser.Id}");
        _cache.Remove($"UserWorkspaces_{newOwnerId}");

        TempData["SuccessMessage"] = $"Successfully transferred workspace management to {targetMember.User.FullName}.";
        return RedirectToPage("/WorkspaceDetail", new { joinCode });
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

        // Viewers are denied by default, others are allowed by default
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
            catch
            {
                // Ignore parsing issues
            }
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
}
