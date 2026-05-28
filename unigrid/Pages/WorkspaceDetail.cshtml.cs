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
        CurrentUserRole = memberRecord?.Role ?? (Workspace.OwnerId == CurrentUser.Id ? "Owner" : "Member");

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

        Files = allFiles.Where(f => f.IsPublic || f.UserId == CurrentUser.Id).ToList();

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

        if (!string.IsNullOrEmpty(NewTaskTitle))
        {
            NewTaskTitle = Helpers.InputSanitizer.SanitizeInput(NewTaskTitle);
            NewTaskDescription = Helpers.InputSanitizer.SanitizeInput(NewTaskDescription);

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

        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.WorkspaceId == Workspace.Id);
        if (task != null)
        {
            // Backend Permission Check
            if (CurrentUserRole != "Owner" && CurrentUserRole != "Manager")
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

            task.Status = status;
            await _context.SaveChangesAsync();
            EvictAllWorkspaceMembersCache(Workspace.Id);
            _logger.LogInformation("Task status updated. TaskId: {TaskId}, NewStatus: {Status}", taskId, status);
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostEditTaskAsync(string joinCode, Guid editTaskId, string editTaskTitle, string editTaskDescription, int editTaskPriority, Guid? editTaskAssigneeId, DateTime? editTaskDueDate)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == editTaskId && t.WorkspaceId == Workspace.Id);
        if (task != null)
        {
            // Backend Permission Check
            // Members can only edit description, while Managers/Owners can edit everything
            if (CurrentUserRole == "Owner" || CurrentUserRole == "Manager")
            {
                task.Title = Helpers.InputSanitizer.SanitizeInput(editTaskTitle);
                task.Priority = editTaskPriority;
                task.AssigneeId = editTaskAssigneeId;
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

        if (!string.IsNullOrEmpty(ChatContent) && ChatRoom != null)
        {
            ChatContent = Helpers.InputSanitizer.SanitizeInput(ChatContent);
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
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        if (UploadedFile == null || UploadedFile.Length == 0)
        {
            TempData["UploadError"] = "No file was selected for upload.";
            return RedirectToPage(new { joinCode });
        }

        string originalFileName = UploadedFile.FileName;
        string baseName = System.IO.Path.GetFileNameWithoutExtension(originalFileName);
        string extension = System.IO.Path.GetExtension(originalFileName).TrimStart('.').ToLower();

        // Validate filename: Not allow regex or . characters in base filename
        if (string.IsNullOrEmpty(baseName))
        {
            TempData["UploadError"] = "Invalid file name.";
            return RedirectToPage(new { joinCode });
        }

        // Check for any dot in the base name
        if (baseName.Contains('.'))
        {
            TempData["UploadError"] = "File name contains invalid characters. Multiple dots ('.') are not allowed in the file name.";
            return RedirectToPage(new { joinCode });
        }

        // Check for special/regex characters (allow only letters, digits, spaces, hyphens, underscores)
        foreach (char c in baseName)
        {
            if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')
            {
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
            TempData["UploadError"] = "File uploads are not allowed on the Free plan. Please upgrade your workspace package to upload files.";
            return RedirectToPage(new { joinCode });
        }

        if (isIndividualStorage && maxStorageLimit <= 0)
        {
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
            TempData["UploadError"] = $"Upload failed. You have exceeded your {typeStr} storage limit of {limitStr} for the {packageTier} plan.";
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
            IsPublic = ShowVisibilityControls ? UploadIsPublic : true,
            CreatedAt = DateTime.UtcNow
        };

        await _context.WorkspaceFiles.AddAsync(file);
        await _context.SaveChangesAsync();
        EvictAllWorkspaceMembersCache(Workspace.Id);
        TempData["UploadSuccess"] = $"Successfully uploaded file: {originalFileName}";
        _logger.LogInformation("File uploaded in workspace {WorkspaceId} by {UserId}", Workspace.Id, CurrentUser.Id);

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostInviteMemberAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        if (!string.IsNullOrEmpty(InviteEmail))
        {
            // Find user profile by email through Account relationship
            var inviteeUser = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.Account.Email == InviteEmail);

            if (inviteeUser != null)
            {
                // Check if already member
                var alreadyMember = await _context.WorkspaceMembers.AnyAsync(wm => wm.WorkspaceId == Workspace.Id && wm.UserId == inviteeUser.Id);
                if (!alreadyMember)
                {
                    // Enforce plan-based maximum member limit checks
                    int currentMembersCount = await _context.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == Workspace.Id);
                    int maxMembersAllowed = 5; // Default for Free and Personal
                    string tier = Workspace.PackageTier ?? "Free";
                    if (tier == "Pro") maxMembersAllowed = 10;
                    else if (tier == "ProPlus") maxMembersAllowed = 15;
                    else if (tier == "Business") maxMembersAllowed = 30;

                    if (currentMembersCount >= maxMembersAllowed)
                    {
                        TempData["InviteError"] = $"Invite failed. This workspace has reached its member limit of {maxMembersAllowed} for the {tier} plan.";
                        return RedirectToPage(new { joinCode });
                    }

                    var newMember = new WorkspaceMember
                    {
                        WorkspaceId = Workspace.Id,
                        UserId = inviteeUser.Id,
                        Role = InviteRole,
                        JoinedAt = DateTime.UtcNow
                    };

                    await _context.WorkspaceMembers.AddAsync(newMember);
                    await _context.SaveChangesAsync();
                    _cache.Remove($"WorkspaceMembers_{Workspace.Id}");
                    _cache.Remove($"UserWorkspaces_{inviteeUser.Id}");
                    _logger.LogInformation("User {Invitee} added as {Role} in Workspace {WorkspaceId}", inviteeUser.FullName, InviteRole, Workspace.Id);
                }
            }
        }

        return RedirectToPage(new { joinCode });
    }

    public string SerializeTask(unigrid.Models.Task task)
    {
        var cleanTask = new {
            id = task.Id,
            title = task.Title,
            description = task.Description,
            status = task.Status,
            priority = task.Priority,
            dueDate = task.DueDate,
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
}
