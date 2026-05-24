using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace unigrid.Pages;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
public class WorkspaceDetailModel : PageModel
{
    private readonly UniGridDbContext _context;
    private readonly ILogger<WorkspaceDetailModel> _logger;

    public WorkspaceDetailModel(UniGridDbContext context, ILogger<WorkspaceDetailModel> logger)
    {
        _context = context;
        _logger = logger;
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

        // Set sidebar workspaces list
        var userWorkspaces = await _context.Workspaces
            .Where(w => w.OwnerId == CurrentUser.Id || w.WorkspaceMembers.Any(m => m.UserId == CurrentUser.Id))
            .ToListAsync();
        ViewData["Workspaces"] = userWorkspaces;

        return Page();
    }

    private async System.Threading.Tasks.Task<bool> LoadWorkspaceDataAsync(string joinCode)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return false;

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile == null) return false;

        CurrentUser = userProfile;
        ViewData["UserName"] = CurrentUser.FullName;
        UserInitials = string.Concat(CurrentUser.FullName.Split(' ').Select(n => n[0]));
        ViewData["UserInitials"] = UserInitials;

        // Fetch Workspace by JoinCode
        Workspace = await _context.Workspaces
            .Include(w => w.Owner)
            .FirstOrDefaultAsync(w => w.JoinCode == joinCode);

        if (Workspace == null) return false;

        var workspaceId = Workspace.Id;

        // Check if user is a member or owner
        var isMember = await _context.WorkspaceMembers.AnyAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == CurrentUser.Id);
        if (Workspace.OwnerId != CurrentUser.Id && !isMember)
        {
            return false;
        }

        // Load Members
        Members = await _context.WorkspaceMembers
            .Include(wm => wm.User)
            .Where(wm => wm.WorkspaceId == workspaceId)
            .ToListAsync();

        var memberRecord = Members.FirstOrDefault(m => m.UserId == CurrentUser.Id);
        CurrentUserRole = memberRecord?.Role ?? (Workspace.OwnerId == CurrentUser.Id ? "Owner" : "Member");

        // Load Tasks
        WorkspaceTasks = await _context.Tasks
            .Include(t => t.Assignee)
            .Include(t => t.Subtasks)
            .Include(t => t.TaskComments)
                .ThenInclude(tc => tc.User)
            .Where(t => t.WorkspaceId == workspaceId)
            .ToListAsync();

        // Load Files
        Files = await _context.WorkspaceFiles
            .Include(wf => wf.User)
            .Where(wf => wf.WorkspaceId == workspaceId)
            .OrderByDescending(wf => wf.CreatedAt)
            .ToListAsync();

        // Load Chat Room & Messages
        ChatRoom = await _context.ChatRooms.FirstOrDefaultAsync(cr => cr.WorkspaceId == workspaceId);
        if (ChatRoom != null)
        {
            ChatMessages = await _context.ChatMessages
                .Include(cm => cm.Sender)
                .Where(cm => cm.RoomId == ChatRoom.Id)
                .OrderBy(cm => cm.SentAt)
                .ToListAsync();
        }

        return true;
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostCreateTaskAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        if (!string.IsNullOrEmpty(NewTaskTitle))
        {
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
            }

            task.Status = status;
            await _context.SaveChangesAsync();
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
                task.Title = editTaskTitle;
                task.Priority = editTaskPriority;
                task.AssigneeId = editTaskAssigneeId;
                task.DueDate = editTaskDueDate;
            }
            
            task.Description = editTaskDescription;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Task edited. TaskId: {TaskId}", editTaskId);
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostAddTaskCommentAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        if (!string.IsNullOrEmpty(CommentContent) && CommentTaskId != Guid.Empty)
        {
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
            _logger.LogInformation("Comment added to task {TaskId} by {UserId}", CommentTaskId, CurrentUser.Id);
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostSendChatMessageAsync(string joinCode, string activeChannel, Guid? selectedFileId)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        if (!string.IsNullOrEmpty(ChatContent) && ChatRoom != null)
        {
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
            _logger.LogInformation("Chat message sent in room {RoomId} in channel {Channel} by {UserId}", ChatRoom.Id, activeChannel, CurrentUser.Id);
        }

        return RedirectToPage(new { joinCode });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUploadFileAsync(string joinCode)
    {
        if (!await LoadWorkspaceDataAsync(joinCode)) return RedirectToPage("/Dashboard");

        if (!string.IsNullOrEmpty(NewFileName))
        {
            var file = new WorkspaceFile
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Workspace.Id,
                UserId = CurrentUser.Id,
                FileName = NewFileName,
                FileUrl = "files/" + NewFileName.ToLower().Replace(" ", "_"),
                FileType = NewFileType,
                FileSize = NewFileSize > 0 ? NewFileSize : 1024 * 1024 * 2, // 2MB default mock
                CreatedAt = DateTime.UtcNow
            };

            await _context.WorkspaceFiles.AddAsync(file);
            await _context.SaveChangesAsync();
            _logger.LogInformation("File uploaded in workspace {WorkspaceId} by {UserId}", Workspace.Id, CurrentUser.Id);
        }

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
                    var newMember = new WorkspaceMember
                    {
                        WorkspaceId = Workspace.Id,
                        UserId = inviteeUser.Id,
                        Role = InviteRole,
                        JoinedAt = DateTime.UtcNow
                    };

                    await _context.WorkspaceMembers.AddAsync(newMember);
                    await _context.SaveChangesAsync();
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
}
