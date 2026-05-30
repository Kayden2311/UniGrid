using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using Microsoft.Extensions.Caching.Memory;

namespace unigrid.Pages;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
public class WorkspacesModel : PageModel
{
    private readonly UniGridDbContext _context;
    private readonly IMemoryCache _cache;

    public WorkspacesModel(UniGridDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public List<Workspace> UserWorkspaces { get; set; } = new();
    public List<WorkspaceFederation> UserFederations { get; set; } = new();
    public List<Workspace> PersonalWorkspaces { get; set; } = new();

    [BindProperty]
    public string NewWorkspaceName { get; set; } = string.Empty;

    [BindProperty]
    public string NewWorkspaceDesc { get; set; } = string.Empty;

    [BindProperty]
    public string FedJoinCode { get; set; } = string.Empty;

    [BindProperty]
    public Guid SelectedPersonalWorkspaceId { get; set; }

    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync()
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;

        if (!string.IsNullOrEmpty(accountIdClaim))
        {
            var accountId = Guid.Parse(accountIdClaim);
            
            // Cache User profile
            var profile = await _cache.GetOrCreateAsync($"User_{accountId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            });
            
            if (profile != null)
            {
                var user = await _context.Users.FindAsync(profile.Id);
                ViewData["UserName"] = user?.FullName ?? string.Empty;
                ViewData["UserInitials"] = user?.FullName != null ? string.Concat(user.FullName.Split(' ').Select(n => n[0])) : string.Empty;

                // Fetch Workspaces owned by or joined by the user, including tasks and member user details (Cache)
                UserWorkspaces = await _cache.GetOrCreateAsync($"UserWorkspaces_{profile.Id}", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                    return await _context.Workspaces
                        .Include(w => w.WorkspaceMembers)
                            .ThenInclude(m => m.User)
                        .Include(w => w.Tasks)
                        .Where(w => w.OwnerId == profile.Id || w.WorkspaceMembers.Any(m => m.UserId == profile.Id))
                        .OrderByDescending(w => w.CreatedAt)
                        .ToListAsync();
                });

                UserFederations = await _context.WorkspaceFederations
                    .Include(f => f.Owner)
                    .Include(f => f.WorkspaceFederationMembers)
                        .ThenInclude(m => m.User)
                    .Include(f => f.WorkspaceFederationMembers)
                        .ThenInclude(m => m.PersonalWorkspace)
                    .Where(f => f.OwnerId == profile.Id || f.WorkspaceFederationMembers.Any(m => m.UserId == profile.Id))
                    .OrderByDescending(f => f.CreatedAt)
                    .ToListAsync();

                PersonalWorkspaces = await _context.Workspaces
                    .Where(w => w.OwnerId == profile.Id)
                    .OrderByDescending(w => w.CreatedAt)
                    .ToListAsync();

                return Page();
            }
        }

        return RedirectToPage("/Login");
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostCreateWorkspaceAsync()
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim))
        {
            return RedirectToPage("/Login");
        }

        var accountId = Guid.Parse(accountIdClaim);
        var profile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (profile == null)
        {
            return RedirectToPage("/Login");
        }

        if (!string.IsNullOrEmpty(NewWorkspaceName))
        {
            // Generate unique 8-character JoinCode
            string joinCode;
            bool isUnique;
            do
            {
                joinCode = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                isUnique = !await _context.Workspaces.AnyAsync(w => w.JoinCode == joinCode);
            } while (!isUnique);

            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                Name = Helpers.InputSanitizer.SanitizeInput(NewWorkspaceName),
                JoinCode = joinCode,
                OwnerId = profile.Id,
                PackageTier = "Free",
                CreatedAt = DateTime.UtcNow
            };

            await _context.Workspaces.AddAsync(workspace);

            // Add owner as a member
            var member = new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = profile.Id,
                Role = "Manager",
                JoinedAt = DateTime.UtcNow
            };
            await _context.WorkspaceMembers.AddAsync(member);

            // Add a default ChatRoom for the workspace
            var chatRoom = new ChatRoom
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                CreatedAt = DateTime.UtcNow
            };
            await _context.ChatRooms.AddAsync(chatRoom);

            await _context.SaveChangesAsync();

            // Evict user workspaces list cache
            _cache.Remove($"UserWorkspaces_{profile.Id}");
        }

        return RedirectToPage("/Workspaces");
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostJoinFederationAsync()
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim))
        {
            return RedirectToPage("/Login");
        }

        var accountId = Guid.Parse(accountIdClaim);
        var profile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (profile == null)
        {
            return RedirectToPage("/Login");
        }

        if (string.IsNullOrWhiteSpace(FedJoinCode) || SelectedPersonalWorkspaceId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please enter a Federation code and select a Personal Workspace to link.";
            return RedirectToPage("/Workspaces");
        }

        var normalizedCode = FedJoinCode.Trim().ToUpper();
        var federation = await _context.WorkspaceFederations
            .FirstOrDefaultAsync(f => f.JoinCode == normalizedCode);

        if (federation == null)
        {
            TempData["ErrorMessage"] = "The Federation code does not exist or has been revoked. Please check again.";
            return RedirectToPage("/Workspaces");
        }

        // Verify if user is already a member of this federation
        var isAlreadyMember = await _context.WorkspaceFederationMembers
            .AnyAsync(m => m.FederationId == federation.Id && m.UserId == profile.Id);

        if (isAlreadyMember)
        {
            TempData["ErrorMessage"] = "You have already joined this Federation.";
            return RedirectToPage("/Workspaces");
        }

        // Verify that the personal workspace is owned by this user
        var personalWorkspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Id == SelectedPersonalWorkspaceId && w.OwnerId == profile.Id);

        if (personalWorkspace == null)
        {
            TempData["ErrorMessage"] = "The selected Personal Workspace is invalid or you are not the owner.";
            return RedirectToPage("/Workspaces");
        }

        // ENFORCE BUSINESS RULE: Link only allowed when BOTH users possess a workspace with subscription as "Personal" plan
        // 1. Check joiner's selected workspace package tier
        if (personalWorkspace.PackageTier != "Personal")
        {
            TempData["ErrorMessage"] = "Link failed! The selected Workspace is not on the Personal plan. Only Personal plan Workspaces can be connected to a Federation.";
            return RedirectToPage("/Workspaces");
        }

        // 2. Check federation creator (owner) package tier (must have at least one workspace with 'Personal' plan)
        var creatorHasPersonalWorkspace = await _context.Workspaces
            .AnyAsync(w => w.OwnerId == federation.OwnerId && w.PackageTier == "Personal");

        if (!creatorHasPersonalWorkspace)
        {
            TempData["ErrorMessage"] = "Link failed! The Federation creator does not own any Personal plan Workspace. Federation rules require both members to own a Personal plan Workspace.";
            return RedirectToPage("/Workspaces");
        }

        // Add user as a member of the federation
        var fedMember = new WorkspaceFederationMember
        {
            FederationId = federation.Id,
            UserId = profile.Id,
            PersonalWorkspaceId = personalWorkspace.Id,
            JoinedAt = DateTime.UtcNow
        };

        await _context.WorkspaceFederationMembers.AddAsync(fedMember);
        
        // Symmetrically set FederationId at the database level for the workspace
        personalWorkspace.FederationId = federation.Id;
        _context.Workspaces.Update(personalWorkspace);

        await _context.SaveChangesAsync();

        // Evict cache to refresh workspaces and federations lists
        _cache.Remove($"UserWorkspaces_{profile.Id}");

        TempData["SuccessMessage"] = $"Successfully connected! Workspace '{personalWorkspace.Name}' has been integrated into Federation '{federation.Name}'.";
        return RedirectToPage("/Workspaces");
    }
}
