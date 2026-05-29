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

        // Stats
        public int TotalChildWorkspaces => ChildWorkspaces.Count;
        public int TotalActiveMembers { get; set; }
        public int TotalTasksDone { get; set; }
        public int TotalFilesCount { get; set; }

        [BindProperty]
        public string NewChildWorkspaceName { get; set; } = string.Empty;

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
            if (CurrentUser == null) return false;

            // Load Federation including Workspaces and Federation Members
            Federation = await _context.WorkspaceFederations
                .Include(f => f.Owner)
                .Include(f => f.Workspaces)
                .Include(f => f.WorkspaceFederationMembers)
                    .ThenInclude(m => m.PersonalWorkspace)
                .FirstOrDefaultAsync(f => f.JoinCode == joinCode.Trim().ToUpper());

            if (Federation == null) return false;

            // Access check: only Federation Owner/Creator (high-level manager) can view the control center
            if (Federation.OwnerId != CurrentUser.Id)
            {
                _logger.LogWarning($"User {CurrentUser.Id} attempted unauthorized access to joint federation {Federation.JoinCode}.");
                return false;
            }

            // Symmetrically query child workspaces: 
            // 1. Direct children (Group/Business) where FederationId == federation.Id
            // 2. Personal workspaces linked via WorkspaceFederationMembers
            var directChildren = Federation.Workspaces.ToList();
            var linkedPersonal = Federation.WorkspaceFederationMembers
                .Where(m => m.PersonalWorkspace != null)
                .Select(m => m.PersonalWorkspace)
                .ToList();

            ChildWorkspaces = directChildren.Concat(linkedPersonal)
                .GroupBy(w => w.Id)
                .Select(g => g.First())
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
                .Where(t => childWorkspaceIds.Contains(t.WorkspaceId) && t.Status == 3)
                .CountAsync();

            // 3. Files uploaded across all child workspaces
            TotalFilesCount = await _context.WorkspaceFiles
                .Where(f => childWorkspaceIds.Contains(f.WorkspaceId))
                .CountAsync();

            // 4. Load shared files
            SharedFiles = await _context.WorkspaceFiles
                .Include(f => f.User)
                .Include(f => f.Workspace)
                .Where(f => childWorkspaceIds.Contains(f.WorkspaceId))
                .OrderByDescending(f => f.CreatedAt)
                .Take(15)
                .ToListAsync();

            // Eligible workspaces to link: Personal workspaces owned by CurrentUser that are NOT already in any federation
            EligibleWorkspacesToLink = await _context.Workspaces
                .Where(w => w.OwnerId == CurrentUser.Id && w.WorkspaceType == "Personal" && w.FederationId == null)
                .OrderBy(w => w.Name)
                .ToListAsync();

            return true;
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostLinkChildWorkspaceAsync(string joinCode, Guid workspaceId)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            var workspace = await _context.Workspaces
                .FirstOrDefaultAsync(w => w.Id == workspaceId && w.OwnerId == CurrentUser.Id);

            if (workspace == null)
            {
                TempData["ErrorMessage"] = "Workspace không tồn tại hoặc bạn không phải chủ sở hữu.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            if (workspace.WorkspaceType != "Personal")
            {
                TempData["ErrorMessage"] = "Chỉ có thể liên kết Workspace cá nhân (Personal Plan) vào Liên bang.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            if (workspace.FederationId != null)
            {
                TempData["ErrorMessage"] = "Workspace này đã thuộc về một Liên bang khác.";
                return RedirectToPage("/FederationDetail", new { joinCode });
            }

            // Perform link
            workspace.FederationId = Federation.Id;
            _context.Workspaces.Update(workspace);

            // Add federation member record automatically for tracking
            var existingMember = await _context.WorkspaceFederationMembers
                .AnyAsync(m => m.FederationId == Federation.Id && m.UserId == CurrentUser.Id);

            if (!existingMember)
            {
                var fedMember = new WorkspaceFederationMember
                {
                    FederationId = Federation.Id,
                    UserId = CurrentUser.Id,
                    PersonalWorkspaceId = workspace.Id,
                    JoinedAt = DateTime.UtcNow
                };
                await _context.WorkspaceFederationMembers.AddAsync(fedMember);
            }

            await _context.SaveChangesAsync();

            // Evict caches
            _cache.Remove($"UserWorkspaces_{CurrentUser.Id}");
            _cache.Remove($"Workspace_{workspace.JoinCode}");

            TempData["SuccessMessage"] = $"Liên kết thành công! Workspace '{workspace.Name}' đã trở thành trực thuộc Liên bang.";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostAddChildWorkspaceAsync(string joinCode)
        {
            var success = await LoadFederationDataAsync(joinCode);
            if (!success) return RedirectToPage("/Workspaces");

            if (string.IsNullOrWhiteSpace(NewChildWorkspaceName))
            {
                TempData["ErrorMessage"] = "Tên Workspace không được để trống.";
                return RedirectToPage("/FederationDetail", new { joinCode });
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

            TempData["SuccessMessage"] = $"Đã tạo và trực thuộc thành công Workspace nhóm '{childWorkspace.Name}' vào Liên bang.";
            return RedirectToPage("/FederationDetail", new { joinCode });
        }
    }
}
