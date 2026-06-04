using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using unigrid.Hubs;
using System.IO;

namespace unigrid.Pages
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
    public class FederationDetailModel : PageModel
    {
        private readonly UniGridDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<FederationDetailModel> _logger;

        public FederationDetailModel(UniGridDbContext context, IMemoryCache cache, ILogger<FederationDetailModel> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        public WorkspaceFederation Federation { get; set; } = null!;
        public User CurrentUser { get; set; } = null!;
        public List<Workspace> ChildWorkspaces { get; set; } = new();
        public List<Workspace> EligibleWorkspacesToLink { get; set; } = new();
        public List<WorkspaceFile> SharedFiles { get; set; } = new();
        public string CurrentUserRole { get; set; } = string.Empty;
        public List<WorkspaceFederationMember> PendingFederationLinks { get; set; } = new();
        public List<WorkspaceFederationMember> ActiveFederationMembers { get; set; } = new();
        public List<unigrid.Models.Task> FederationTasks { get; set; } = new();
        public List<KpiTarget> ChildKpiTargets { get; set; } = new();
        public List<unigrid.Models.Task> ChildTasks { get; set; } = new();
        public List<TaskCategory> ChildCategories { get; set; } = new();
        public List<WorkspaceMember> ChildWorkspaceMembers { get; set; } = new();
        public List<User> FederationUsers { get; set; } = new();
        public ChatRoom? FederationChatRoom { get; set; }
        public List<ChatMessage> FederationChatMessages { get; set; } = new();
        public List<WorkspaceFile> PushableFiles { get; set; } = new();

        // Stats
        public int TotalChildWorkspaces => ChildWorkspaces.Count;
        public int TotalActiveMembers { get; set; }
        public int TotalTasksDone { get; set; }
        public int TotalFilesCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string ActiveTab { get; set; } = "overview";

        [BindProperty]
        public string NewChildWorkspaceName { get; set; } = string.Empty;

        [BindProperty]
        public Microsoft.AspNetCore.Http.IFormFile? UploadedFederationFile { get; set; }

        public async System.Threading.Tasks.Task<IActionResult> OnGetAsync(string joinCode)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success)
            {
                return RedirectToPage("/Workspaces");
            }

            return Page();
        }

        private async System.Threading.Tasks.Task<bool> LoadFederationDataAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode)) return false;

            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return false;

            var accountId = Guid.Parse(accountIdClaim);
            CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (CurrentUser == null)
            {
                var accountRecord = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
                if (accountRecord != null)
                {
                    var parts = accountRecord.Email.Split('@')[0].Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                    var fullNameParts = parts.Select(n => n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1).ToLower() : string.Empty);
                    var parsedName = string.Join(" ", fullNameParts);
                    if (string.IsNullOrWhiteSpace(parsedName)) parsedName = "User";

                    CurrentUser = new User
                    {
                        Id = Guid.NewGuid(),
                        AccountId = accountId,
                        FullName = parsedName,
                        SubscriptionTier = "Free"
                    };
                    await _context.Users.AddAsync(CurrentUser);
                    await _context.SaveChangesAsync();
                }
            }

            if (CurrentUser == null) return false;

            // Load Federation including Workspaces and Federation Members
            Federation = await _context.WorkspaceFederations
                .Include(f => f.Owner)
                .Include(f => f.Workspaces)
                .Include(f => f.WorkspaceFederationMembers)
                    .ThenInclude(m => m.PersonalWorkspace)
                .Include(f => f.WorkspaceFederationMembers)
                    .ThenInclude(m => m.User)
                        .ThenInclude(u => u.Account)
                .FirstOrDefaultAsync(f => f.JoinCode == joinCode.Trim().ToUpper());

            if (Federation == null) return false;

            // Access check: Federation Owner or active Federation Member
            var memberRecord = Federation.WorkspaceFederationMembers.FirstOrDefault(m => m.UserId == CurrentUser.Id);
            bool isOwner = Federation.OwnerId == CurrentUser.Id;
            bool isActiveMember = memberRecord != null && memberRecord.Status == "Active";

            if (!isOwner && !isActiveMember)
            {
                _logger.LogWarning($"User {CurrentUser.Id} attempted unauthorized access to joint federation {Federation.JoinCode}.");
                return false;
            }

            CurrentUserRole = isOwner ? "Owner" : (memberRecord?.Role ?? "Member");

            // Symmetrically query child workspaces: 
            // 1. Direct children (Group/Business) where FederationId == federation.Id
            // 2. Personal workspaces linked via WorkspaceFederationMembers with status 'Active'
            var directChildren = Federation.Workspaces.ToList();
            var linkedPersonal = Federation.WorkspaceFederationMembers
                .Where(m => m.PersonalWorkspace != null && m.Status == "Active")
                .Select(m => m.PersonalWorkspace)
                .ToList();

            ChildWorkspaces = directChildren.Concat(linkedPersonal)
                .GroupBy(w => w.Id)
                .Select(g => g.First())
                .ToList();

            PendingFederationLinks = Federation.WorkspaceFederationMembers
                .Where(m => m.Status == "PendingOwnerApproval")
                .ToList();

            ActiveFederationMembers = Federation.WorkspaceFederationMembers
                .Where(m => m.Status == "Active")
                .ToList();

            var childWorkspaceIds = ChildWorkspaces.Select(w => w.Id).ToList();

            // Calculate Aggregate Stats
            // 1. Active members (distinct user IDs joined across all child workspaces)
            TotalActiveMembers = await _context.WorkspaceMembers
                .Where(m => childWorkspaceIds.Contains(m.WorkspaceId))
                .Select(m => m.UserId)
                .Distinct()
                .CountAsync();

            // 2. Tasks completed (status == 3) across all child workspaces
            TotalTasksDone = await _context.Tasks
                .Where(t => t.WorkspaceId.HasValue && childWorkspaceIds.Contains(t.WorkspaceId.Value) && t.Status == 3)
                .CountAsync();

            // 3. Files uploaded across all child workspaces
            TotalFilesCount = await _context.WorkspaceFiles
                .Where(f => f.FederationId == Federation.Id || (f.WorkspaceId.HasValue && childWorkspaceIds.Contains(f.WorkspaceId.Value)))
                .CountAsync();

            // 4. Load shared files
            SharedFiles = await _context.WorkspaceFiles
                .Include(f => f.User)
                .Include(f => f.Workspace)
                .Where(f => f.FederationId == Federation.Id || (f.WorkspaceId.HasValue && childWorkspaceIds.Contains(f.WorkspaceId.Value)))
                .OrderByDescending(f => f.CreatedAt)
                .Take(15)
                .ToListAsync();

            // Eligible workspaces to link: Group workspaces owned by CurrentUser that are NOT already in any federation
            EligibleWorkspacesToLink = await _context.Workspaces
                .Where(w => w.OwnerId == CurrentUser.Id && w.WorkspaceType == "Group" && w.FederationId == null)
                .OrderBy(w => w.Name)
                .ToListAsync();

            FederationTasks = await _context.Tasks
                .Include(t => t.Assignee)
                .Where(t => t.FederationId == Federation.Id && t.WorkspaceId == null)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            ChildKpiTargets = await _context.KpiTargets
                .Include(t => t.User)
                .Include(t => t.Category)
                .Where(t => childWorkspaceIds.Contains(t.WorkspaceId))
                .ToListAsync();

            ChildTasks = await _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.Category)
                .Where(t => t.WorkspaceId.HasValue && childWorkspaceIds.Contains(t.WorkspaceId.Value))
                .ToListAsync();

            ChildCategories = await _context.TaskCategories
                .Where(c => childWorkspaceIds.Contains(c.WorkspaceId))
                .ToListAsync();

            ChildWorkspaceMembers = await _context.WorkspaceMembers
                .Include(m => m.User)
                .Include(m => m.Workspace)
                .Where(m => childWorkspaceIds.Contains(m.WorkspaceId))
                .ToListAsync();

            FederationUsers = Federation.WorkspaceFederationMembers
                .Where(m => m.Status == "Active")
                .Select(m => m.User)
                .ToList();

            if (!FederationUsers.Any(u => u.Id == Federation.OwnerId))
            {
                FederationUsers.Insert(0, Federation.Owner);
            }

            FederationChatRoom = await _context.ChatRooms.FirstOrDefaultAsync(cr => cr.FederationId == Federation.Id);
            if (FederationChatRoom != null)
            {
                FederationChatMessages = await _context.ChatMessages
                    .Include(cm => cm.Sender)
                    .Where(cm => cm.RoomId == FederationChatRoom.Id)
                    .OrderBy(cm => cm.SentAt)
                    .ToListAsync();
            }

            // Find workspaces where CurrentUser is Owner or Member
            var userWorkspaceIds = await _context.Workspaces
                .Where(w => w.OwnerId == CurrentUser.Id)
                .Select(w => w.Id)
                .Union(
                    _context.WorkspaceMembers
                        .Where(m => m.UserId == CurrentUser.Id)
                        .Select(m => m.WorkspaceId)
                )
                .ToListAsync();

            // Get files from those workspaces that are not already in this federation
            PushableFiles = await _context.WorkspaceFiles
                .Include(f => f.Workspace)
                .Include(f => f.User)
                .Where(f => f.WorkspaceId.HasValue && userWorkspaceIds.Contains(f.WorkspaceId.Value) && f.FederationId != Federation.Id)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            return true;
        }

        public async System.Threading.Tasks.Task<System.Text.Json.Nodes.JsonObject> GetFederationSettingsAsync(string joinCode)
        {
            if (string.IsNullOrEmpty(joinCode)) return new System.Text.Json.Nodes.JsonObject();
            
            string cacheKey = $"FedSettings_{joinCode.Trim().ToUpper()}";
            if (!_cache.TryGetValue(cacheKey, out System.Text.Json.Nodes.JsonObject? settings))
            {
                var fed = await _context.WorkspaceFederations
                    .Where(f => f.JoinCode == joinCode)
                    .Select(f => new { f.SettingsJson })
                    .FirstOrDefaultAsync();

                if (fed != null && !string.IsNullOrEmpty(fed.SettingsJson))
                {
                    try
                    {
                        var parsed = System.Text.Json.Nodes.JsonNode.Parse(fed.SettingsJson);
                        if (parsed is System.Text.Json.Nodes.JsonObject obj)
                        {
                            settings = obj;
                        }
                    }
                    catch {}
                }

                if (settings == null)
                {
                    settings = new System.Text.Json.Nodes.JsonObject
                    {
                        ["allowMemberCreateTask"] = false,
                        ["allowManagerSetKpi"] = true,
                        ["allowMemberPushFile"] = true,
                        ["allowMemberChat"] = true
                    };
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(10));
                _cache.Set(cacheKey, settings, cacheOptions);
            }

            return settings ?? new System.Text.Json.Nodes.JsonObject();
        }

        public async System.Threading.Tasks.Task<bool> GetSettingValueAsync(string joinCode, string key, bool defaultValue)
        {
            var settings = await GetFederationSettingsAsync(joinCode);
            if (settings != null && settings[key] != null)
            {
                return settings[key]?.GetValue<bool>() ?? defaultValue;
            }
            return defaultValue;
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateFederationSettingsAsync(string joinCode, bool allowMemberCreateTask, bool allowManagerSetKpi, bool allowMemberPushFile, bool allowMemberChat)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            if (CurrentUserRole != "Owner" && CurrentUserRole != "HeadPresident")
            {
                TempData["ErrorMessage"] = "You do not have permission to manage federation settings.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            var newSettings = new System.Text.Json.Nodes.JsonObject
            {
                ["allowMemberCreateTask"] = allowMemberCreateTask,
                ["allowManagerSetKpi"] = allowManagerSetKpi,
                ["allowMemberPushFile"] = allowMemberPushFile,
                ["allowMemberChat"] = allowMemberChat
            };

            Federation.SettingsJson = newSettings.ToJsonString();
            _context.WorkspaceFederations.Update(Federation);
            await _context.SaveChangesAsync();

            // Evict cache
            _cache.Remove($"FedSettings_{joinCode.Trim().ToUpper()}");

            TempData["SuccessMessage"] = "Federation settings updated successfully.";
            return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
        }

        public int GetKpiActualValue(KpiTarget target)
        {
            var tasks = ChildTasks.Where(t => 
                t.WorkspaceId == target.WorkspaceId &&
                t.AssigneeId == target.UserId && 
                t.CategoryId == target.CategoryId && 
                t.DueDate.HasValue && 
                t.DueDate.Value >= target.StartDate && 
                t.DueDate.Value <= target.EndDate).ToList();

            int actual = 0;
            foreach (var task in tasks)
            {
                if (task.IsCounterTask)
                {
                    actual += task.CurrentCount;
                }
                else if (task.Status == 3)
                {
                    actual += 1;
                }
            }
            return actual;
        }

        public string SerializeFederationChatMessages()
        {
            var cleanMessages = FederationChatMessages.Select(cm => new
            {
                id = cm.Id,
                roomId = cm.RoomId,
                senderId = cm.SenderId,
                senderName = cm.Sender.FullName,
                content = cm.Content,
                rawContent = cm.Content,
                sentAt = cm.SentAt,
                channel = "general"
            }).ToList();

            return System.Text.Json.JsonSerializer.Serialize(cleanMessages, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostLinkChildWorkspaceAsync(string joinCode, string inviteCode)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            if (string.IsNullOrWhiteSpace(inviteCode))
            {
                TempData["ErrorMessage"] = "Please enter the workspace invite code.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            if (!Guid.TryParse(inviteCode.Trim(), out var inviteGuid))
            {
                TempData["ErrorMessage"] = "Invite code format is invalid (UUID expected).";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            var workspace = await _context.Workspaces
                .FirstOrDefaultAsync(w => w.InviteCode == inviteGuid);

            if (workspace == null)
            {
                TempData["ErrorMessage"] = "Workspace with this invite code does not exist.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            if (workspace.WorkspaceType == "Personal")
            {
                TempData["ErrorMessage"] = "This workspace is Personal. Please use the Personal Workspace link form.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            if (workspace.FederationId != null)
            {
                TempData["ErrorMessage"] = "This workspace already belongs to another federation.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // Perform link
            workspace.FederationId = Federation.Id;
            _context.Workspaces.Update(workspace);

            // Add federation member record automatically for tracking
            var existingMember = await _context.WorkspaceFederationMembers
                .AnyAsync(m => m.FederationId == Federation.Id && m.UserId == workspace.OwnerId);

            if (!existingMember)
            {
                var fedMember = new WorkspaceFederationMember
                {
                    FederationId = Federation.Id,
                    UserId = workspace.OwnerId,
                    JoinedAt = DateTime.UtcNow,
                    Role = "Member",
                    Status = "Active"
                };
                await _context.WorkspaceFederationMembers.AddAsync(fedMember);
            }

            await _context.SaveChangesAsync();

            // Evict caches
            _cache.Remove($"UserWorkspaces_{workspace.OwnerId}");
            _cache.Remove($"Workspace_{workspace.JoinCode}");

            TempData["SuccessMessage"] = $"Successfully linked! Workspace '{workspace.Name}' is now part of the Federation.";
            return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostLinkPersonalWorkspaceByCodeAsync(string joinCode, string inviteCode)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            if (string.IsNullOrWhiteSpace(inviteCode))
            {
                TempData["ErrorMessage"] = "Please enter the personal workspace invite code.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            if (!Guid.TryParse(inviteCode.Trim(), out var inviteGuid))
            {
                TempData["ErrorMessage"] = "Invite code format is invalid (UUID expected).";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // Find the workspace by InviteCode
            var workspace = await _context.Workspaces
                .FirstOrDefaultAsync(w => w.InviteCode == inviteGuid);

            if (workspace == null)
            {
                TempData["ErrorMessage"] = "Personal workspace with this invite code does not exist.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            if (workspace.WorkspaceType != "Personal")
            {
                TempData["ErrorMessage"] = "This invite code belongs to a Group workspace. Only Personal workspaces can be linked manually.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            if (workspace.FederationId != null)
            {
                TempData["ErrorMessage"] = "This personal workspace already belongs to another federation.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // Verify if a mapping already exists
            var existingMember = await _context.WorkspaceFederationMembers
                .FirstOrDefaultAsync(m => m.FederationId == Federation.Id && m.PersonalWorkspaceId == workspace.Id);

            if (existingMember != null)
            {
                TempData["ErrorMessage"] = $"This workspace has already submitted a link request (Status: {existingMember.Status}).";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // Create a pending member record for the Federation Owner to approve
            var fedMember = new WorkspaceFederationMember
            {
                FederationId = Federation.Id,
                UserId = workspace.OwnerId, 
                PersonalWorkspaceId = workspace.Id,
                JoinedAt = DateTime.UtcNow,
                Role = "Member",
                Status = "PendingOwnerApproval" 
            };

            await _context.WorkspaceFederationMembers.AddAsync(fedMember);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Link request for Workspace '{workspace.Name}' submitted successfully. Awaiting Federation Owner approval.";
            return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostApprovePersonalWorkspaceAsync(string joinCode, Guid personalWorkspaceId)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            // Only Federation Owner can approve
            if (Federation.OwnerId != CurrentUser.Id)
            {
                TempData["ErrorMessage"] = "Only the Federation Owner has permission to approve link requests.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            var pendingMember = await _context.WorkspaceFederationMembers
                .FirstOrDefaultAsync(m => m.FederationId == Federation.Id && m.PersonalWorkspaceId == personalWorkspaceId && m.Status == "PendingOwnerApproval");

            if (pendingMember == null)
            {
                TempData["ErrorMessage"] = "Link request does not exist or has already been processed.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // Approve: set status to Active
            pendingMember.Status = "Active";
            _context.WorkspaceFederationMembers.Update(pendingMember);

            // Symmetrically set FederationId at the workspace level
            var workspace = await _context.Workspaces.FindAsync(personalWorkspaceId);
            if (workspace != null)
            {
                workspace.FederationId = Federation.Id;
                _context.Workspaces.Update(workspace);
            }

            await _context.SaveChangesAsync();

            // Clear cache
            if (workspace != null)
            {
                _cache.Remove($"Workspace_{workspace.JoinCode}");
            }
            _cache.Remove($"UserWorkspaces_{pendingMember.UserId}");

            TempData["SuccessMessage"] = $"Approved link request for Workspace '{workspace?.Name ?? "Personal"}'.";
            return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostRejectPersonalWorkspaceAsync(string joinCode, Guid personalWorkspaceId)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            // Only Federation Owner can reject
            if (Federation.OwnerId != CurrentUser.Id)
            {
                TempData["ErrorMessage"] = "Only the Federation Owner has permission to reject link requests.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            var pendingMember = await _context.WorkspaceFederationMembers
                .FirstOrDefaultAsync(m => m.FederationId == Federation.Id && m.PersonalWorkspaceId == personalWorkspaceId && m.Status == "PendingOwnerApproval");

            if (pendingMember == null)
            {
                TempData["ErrorMessage"] = "Link request does not exist or has already been processed.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // Reject: delete the pending member record
            _context.WorkspaceFederationMembers.Remove(pendingMember);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Decline and removed link request successfully.";
            return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostAddChildWorkspaceAsync(string joinCode)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            if (string.IsNullOrWhiteSpace(NewChildWorkspaceName))
            {
                TempData["ErrorMessage"] = "Workspace name cannot be empty.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // Generate unique 8-character JoinCode
            string childJoinCode;
            bool isUnique;
            do
            {
                childJoinCode = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                isUnique = !await _context.Workspaces.AnyAsync(w => w.JoinCode == childJoinCode);
            } while (!isUnique);

            var childWorkspace = new Workspace
            {
                Id = Guid.NewGuid(),
                Name = Helpers.InputSanitizer.SanitizeInput(NewChildWorkspaceName),
                JoinCode = childJoinCode,
                OwnerId = CurrentUser.Id,
                PackageTier = "ProPlus", // Default for Group child workspace
                WorkspaceType = "Group",
                FederationId = Federation.Id,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Workspaces.AddAsync(childWorkspace);

            // Add owner as Manager
            var member = new WorkspaceMember
            {
                WorkspaceId = childWorkspace.Id,
                UserId = CurrentUser.Id,
                Role = "Manager",
                JoinedAt = DateTime.UtcNow
            };
            await _context.WorkspaceMembers.AddAsync(member);

            // Default ChatRoom
            var chatRoom = new ChatRoom
            {
                Id = Guid.NewGuid(),
                WorkspaceId = childWorkspace.Id,
                CreatedAt = DateTime.UtcNow
            };
            await _context.ChatRooms.AddAsync(chatRoom);

            await _context.SaveChangesAsync();

            // Evict cache to refresh workspaces and sidebar lists
            _cache.Remove($"UserWorkspaces_{CurrentUser.Id}");

            TempData["SuccessMessage"] = $"Successfully created and linked group Workspace '{childWorkspace.Name}' to the Federation.";
            return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostCreateFederationTaskAsync(
            string joinCode, string title, string description, int priority, Guid? assigneeId, DateTime? dueDate, int status, bool isCounterTask = false, int targetCount = 1)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            var settings = await GetFederationSettingsAsync(joinCode);
            bool canCreate = CurrentUserRole == "Owner" || CurrentUserRole == "HeadPresident" ||
                             (settings["allowMemberCreateTask"]?.GetValue<bool>() == true);
            if (!canCreate)
            {
                TempData["ErrorMessage"] = "You do not have permission to assign tasks in this federation.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["ErrorMessage"] = "Task title is required.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            var task = new unigrid.Models.Task
            {
                Id = Guid.NewGuid(),
                WorkspaceId = null,
                FederationId = Federation.Id,
                Title = Helpers.InputSanitizer.SanitizeInput(title),
                Description = Helpers.InputSanitizer.SanitizeInput(description),
                Priority = priority,
                AssigneeId = assigneeId,
                DueDate = dueDate,
                Status = status,
                CreatedAt = DateTime.UtcNow,
                IsCounterTask = isCounterTask,
                TargetCount = targetCount,
                CurrentCount = 0
            };

            await _context.Tasks.AddAsync(task);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Successfully created and assigned federation task.";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateFederationTaskStatusAsync(string joinCode, Guid taskId, int status)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.FederationId == Federation.Id);
            if (task == null)
            {
                TempData["ErrorMessage"] = "Federation task not found.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            // Access check: only owner, head president, or assignee can change status
            if (CurrentUserRole != "Owner" && CurrentUserRole != "HeadPresident" && task.AssigneeId != CurrentUser.Id)
            {
                TempData["ErrorMessage"] = "You do not have permission to update the status of this task.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            task.Status = status;
            _context.Tasks.Update(task);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Federation task status updated successfully.";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteFederationTaskAsync(string joinCode, Guid taskId)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            if (CurrentUserRole != "Owner" && CurrentUserRole != "HeadPresident")
            {
                TempData["ErrorMessage"] = "You do not have permission to delete federation tasks.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.FederationId == Federation.Id);
            if (task != null)
            {
                _context.Tasks.Remove(task);
                await _context.SaveChangesAsync();

                var hubContext = (IHubContext<ChatHub>)HttpContext.RequestServices.GetService(typeof(IHubContext<ChatHub>));
                if (hubContext != null)
                {
                    await hubContext.Clients.Group(Federation.Id.ToString()).SendAsync("ReceiveTaskDeletion", new { taskId = taskId });
                }
            }

            TempData["SuccessMessage"] = "Federation task deleted successfully.";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostCreateChildKpiTargetAsync(
            string joinCode, Guid targetWorkspaceId, Guid targetUserId, Guid targetCategoryId, string periodType, DateTime startDate, DateTime endDate, int targetValue)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            var settings = await GetFederationSettingsAsync(joinCode);
            bool canSetKpi = CurrentUserRole == "Owner" || CurrentUserRole == "HeadPresident" ||
                             (settings["allowManagerSetKpi"]?.GetValue<bool>() == true && CurrentUserRole == "DepartmentManager");
            if (!canSetKpi)
            {
                TempData["ErrorMessage"] = "You do not have permission to configure KPI targets.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            if (targetValue <= 0)
            {
                TempData["ErrorMessage"] = "KPI target value must be greater than 0.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            var target = new KpiTarget
            {
                Id = Guid.NewGuid(),
                WorkspaceId = targetWorkspaceId,
                UserId = targetUserId,
                CategoryId = targetCategoryId,
                PeriodType = periodType,
                StartDate = startDate,
                EndDate = endDate,
                TargetValue = targetValue,
                CreatedAt = DateTime.UtcNow
            };

            await _context.KpiTargets.AddAsync(target);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "KPI target configured successfully.";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteChildKpiTargetAsync(string joinCode, Guid targetId)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            if (CurrentUserRole != "Owner" && CurrentUserRole != "HeadPresident")
            {
                TempData["ErrorMessage"] = "You do not have permission to delete KPI targets.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            var target = await _context.KpiTargets.FindAsync(targetId);
            if (target != null)
            {
                _context.KpiTargets.Remove(target);
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = "KPI target deleted successfully.";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostSendFederationChatMessageAsync(string joinCode, string content)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            var settings = await GetFederationSettingsAsync(joinCode);
            bool canChat = CurrentUserRole == "Owner" || CurrentUserRole == "HeadPresident" || CurrentUserRole == "DepartmentManager" ||
                           (settings["allowMemberChat"]?.GetValue<bool>() == true);
            if (!canChat)
            {
                return new BadRequestObjectResult(new { message = "Chat has been disabled for members in this federation." });
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return new BadRequestObjectResult(new { message = "Message content cannot be empty." });
            }

            var chatRoom = await _context.ChatRooms.FirstOrDefaultAsync(cr => cr.FederationId == Federation.Id);
            if (chatRoom == null)
            {
                return new BadRequestObjectResult(new { message = "Federation chat room does not exist." });
            }

            var message = new ChatMessage
            {
                Id = Guid.NewGuid(),
                RoomId = chatRoom.Id,
                SenderId = CurrentUser.Id,
                Content = Helpers.InputSanitizer.SanitizeInput(content),
                SentAt = DateTime.UtcNow,
                IsDeleted = false
            };

            await _context.ChatMessages.AddAsync(message);
            await _context.SaveChangesAsync();

            var payload = new
            {
                id = message.Id,
                roomId = message.RoomId,
                senderId = message.SenderId,
                senderName = CurrentUser.FullName,
                content = message.Content,
                rawContent = message.Content,
                sentAt = message.SentAt,
                channel = "general"
            };

            var hubContext = (IHubContext<ChatHub>)HttpContext.RequestServices.GetService(typeof(IHubContext<ChatHub>));
            if (hubContext != null)
            {
                await hubContext.Clients.Group(Federation.Id.ToString()).SendAsync("ReceiveChatMessage", payload);
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new JsonResult(payload);
            }

            return RedirectToPage(new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteFederationChatMessageAsync(string joinCode, Guid messageId)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            var message = await _context.ChatMessages.Include(cm => cm.Sender).FirstOrDefaultAsync(m => m.Id == messageId);
            if (message == null)
            {
                return new BadRequestObjectResult(new { message = "Message not found." });
            }

            bool isSender = message.SenderId == CurrentUser.Id;
            bool isOwner = Federation.OwnerId == CurrentUser.Id || CurrentUserRole == "HeadPresident";
            if (!isSender && !isOwner)
            {
                return Forbid();
            }

            message.IsDeleted = true;
            message.Content = "[deleted_message]" + message.Content;
            _context.ChatMessages.Update(message);
            await _context.SaveChangesAsync();

            var hubContext = (IHubContext<ChatHub>)HttpContext.RequestServices.GetService(typeof(IHubContext<ChatHub>));
            if (hubContext != null)
            {
                await hubContext.Clients.Group(Federation.Id.ToString()).SendAsync("ReceiveMessageDeletion", new { messageId = messageId });
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new JsonResult(new { success = true, messageId = messageId });
            }

            return RedirectToPage(new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostUploadFederationFileAsync(string joinCode)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            bool canUpload = CurrentUserRole == "Owner" || CurrentUserRole == "HeadPresident" || CurrentUserRole == "DepartmentManager";
            if (!canUpload)
            {
                TempData["ErrorMessage"] = "You do not have permission to upload files directly to the federation.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            if (UploadedFederationFile == null || UploadedFederationFile.Length == 0)
            {
                TempData["ErrorMessage"] = "No file selected.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            string originalFileName = UploadedFederationFile.FileName;
            string baseName = Path.GetFileNameWithoutExtension(originalFileName);
            string extension = Path.GetExtension(originalFileName).TrimStart('.').ToLower();

            if (string.IsNullOrEmpty(baseName))
            {
                TempData["ErrorMessage"] = "Invalid file name.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            foreach (char c in baseName)
            {
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')
                {
                    TempData["ErrorMessage"] = "File name contains invalid characters. Only letters, numbers, spaces, hyphens, and underscores are allowed.";
                    return RedirectToPage("/FederationDetail", new { joinCode });
                }
            }

            string fileType = "doc";
            if (extension == "pdf") fileType = "pdf";
            else if (extension == "xls" || extension == "xlsx" || extension == "csv") fileType = "spreadsheet";
            else if (extension == "png" || extension == "jpg" || extension == "jpeg" || extension == "gif" || extension == "svg") fileType = "image";

            string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "files", "federations", Federation.Id.ToString());
            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
            }

            string safeFileName = originalFileName.ToLower().Replace(" ", "_");
            string physicalPath = Path.Combine(uploadDir, safeFileName);

            using (var stream = new FileStream(physicalPath, FileMode.Create))
            {
                await UploadedFederationFile.CopyToAsync(stream);
            }

            var file = new WorkspaceFile
            {
                Id = Guid.NewGuid(),
                WorkspaceId = null,
                FederationId = Federation.Id,
                UserId = CurrentUser.Id,
                FileName = originalFileName,
                FileUrl = $"files/federations/{Federation.Id}/{safeFileName}",
                FileType = fileType,
                FileSize = UploadedFederationFile.Length,
                IsPublic = true,
                CreatedAt = DateTime.UtcNow
            };

            await _context.WorkspaceFiles.AddAsync(file);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Successfully uploaded file: {originalFileName}";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostPushFileToFederationAsync(string joinCode, Guid fileId)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            var settings = await GetFederationSettingsAsync(joinCode);
            bool canPush = CurrentUserRole == "Owner" || CurrentUserRole == "HeadPresident" || CurrentUserRole == "DepartmentManager" ||
                           (settings["allowMemberPushFile"]?.GetValue<bool>() == true);
            if (!canPush)
            {
                TempData["ErrorMessage"] = "You do not have permission to push files to the federation.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            var file = await _context.WorkspaceFiles.FirstOrDefaultAsync(f => f.Id == fileId);
            if (file == null)
            {
                TempData["ErrorMessage"] = "File to push not found.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            if (!file.WorkspaceId.HasValue)
            {
                TempData["ErrorMessage"] = "This file does not belong to any child workspace.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            var isOwnerOrMember = await _context.Workspaces.AnyAsync(w => w.Id == file.WorkspaceId.Value && w.OwnerId == CurrentUser.Id) ||
                                   await _context.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == file.WorkspaceId.Value && m.UserId == CurrentUser.Id);

            if (!isOwnerOrMember)
            {
                TempData["ErrorMessage"] = "You do not have permission to push this file because you are not a member of the workspace containing it.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            file.FederationId = Federation.Id;
            _context.WorkspaceFiles.Update(file);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Successfully pushed file '{file.FileName}' to the federation.";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateFederationMemberRoleAsync(string joinCode, Guid userId, string role)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            // Authorization: only Owner or HeadPresident can manage members
            if (CurrentUserRole != "Owner" && CurrentUserRole != "HeadPresident")
            {
                TempData["ErrorMessage"] = "You do not have permission to manage federation members.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            if (userId == Federation.OwnerId)
            {
                TempData["ErrorMessage"] = "Cannot change the role of the Federation Owner.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            var member = await _context.WorkspaceFederationMembers
                .FirstOrDefaultAsync(m => m.FederationId == Federation.Id && m.UserId == userId);

            if (member == null)
            {
                TempData["ErrorMessage"] = "Federation member not found.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // HeadPresidents cannot modify other HeadPresidents or the Owner
            if (CurrentUserRole == "HeadPresident" && (member.Role == "HeadPresident" || member.Role == "Owner"))
            {
                TempData["ErrorMessage"] = "Head Presidents cannot modify the roles of other Head Presidents or the Owner.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            member.Role = role;
            _context.WorkspaceFederationMembers.Update(member);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Federation member role updated successfully.";
            return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostRemoveFederationMemberAsync(string joinCode, Guid userId)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            // Authorization: only Owner or HeadPresident can manage members
            if (CurrentUserRole != "Owner" && CurrentUserRole != "HeadPresident")
            {
                TempData["ErrorMessage"] = "You do not have permission to manage federation members.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            if (userId == Federation.OwnerId)
            {
                TempData["ErrorMessage"] = "Cannot remove the Federation Owner.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            var member = await _context.WorkspaceFederationMembers
                .FirstOrDefaultAsync(m => m.FederationId == Federation.Id && m.UserId == userId);

            if (member == null)
            {
                TempData["ErrorMessage"] = "Federation member not found.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            // HeadPresidents cannot remove other HeadPresidents
            if (CurrentUserRole == "HeadPresident" && member.Role == "HeadPresident")
            {
                TempData["ErrorMessage"] = "Head Presidents cannot remove other Head Presidents.";
                return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
            }

            _context.WorkspaceFederationMembers.Remove(member);

            // Sever connection: set FederationId to null for any workspaces owned by this user linked to this federation
            var userWorkspaces = await _context.Workspaces
                .Where(w => w.OwnerId == userId && w.FederationId == Federation.Id)
                .ToListAsync();

            foreach (var w in userWorkspaces)
            {
                w.FederationId = null;
                _context.Workspaces.Update(w);
            }

            await _context.SaveChangesAsync();

            // Evict caches
            _cache.Remove($"UserWorkspaces_{userId}");

            TempData["SuccessMessage"] = "Removed member from the Federation and unlinked their child Workspaces.";
            return RedirectToPage("/FederationDetail", new { joinCode, activeTab = "settings" });
        }
    }
}
