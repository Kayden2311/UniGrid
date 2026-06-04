using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System.Security.Claims;

namespace unigrid.Pages;

public class PricingModel : PageModel
{
    private readonly UniGridDbContext _context;

    public PricingModel(UniGridDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public string? JoinCode { get; set; }

    public string? ActiveWorkspaceJoinCode { get; set; }
    public bool IsWorkspaceUpgrade { get; set; }

    public string CurrentPlan { get; set; } = "None";

    public int GetPlanRank(string? plan)
    {
        return plan switch
        {
            "Personal" => 1,
            "Pro" => 2,
            "ProPlus" => 3,
            "Business" => 4,
            _ => 0
        };
    }

    public async System.Threading.Tasks.Task OnGetAsync()
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (!string.IsNullOrEmpty(accountIdClaim))
        {
            var accountId = Guid.Parse(accountIdClaim);
            var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (userProfile != null)
            {
                CurrentPlan = userProfile.SubscriptionTier ?? "Free";
                ViewData["UserName"] = userProfile.FullName;
                ViewData["UserInitials"] = string.Concat(userProfile.FullName.Split(' ').Select(n => n[0]));

                // Fetch Workspaces for sidebar
                var userWorkspaces = await _context.Workspaces
                    .Where(w => w.OwnerId == userProfile.Id || w.WorkspaceMembers.Any(m => m.UserId == userProfile.Id))
                    .ToListAsync();
                ViewData["Workspaces"] = userWorkspaces;

                // If workspace context is provided, align pricing page with workspace's active package tier
                if (!string.IsNullOrEmpty(JoinCode))
                {
                    var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.JoinCode == JoinCode);
                    if (workspace != null)
                    {
                        CurrentPlan = workspace.PackageTier ?? "Free";
                        ActiveWorkspaceJoinCode = JoinCode;
                        IsWorkspaceUpgrade = true;
                    }
                }
            }
        }
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpgradeAsync(string tier)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return RedirectToPage("/Login");

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile != null)
        {
            // If upgrading an active workspace, update workspace's package tier directly
            if (!string.IsNullOrEmpty(JoinCode))
            {
                var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.JoinCode == JoinCode);
                if (workspace != null)
                {
                    if (tier == "Personal")
                    {
                        int memberCount = await _context.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == workspace.Id);
                        if (memberCount > 1)
                        {
                            TempData["UpgradeError"] = "Cannot switch this Workspace to the Personal plan because it currently has more than 1 member. The Personal plan is for individual use only.";
                            return RedirectToPage("/Pricing", new { joinCode = JoinCode });
                        }
                    }
                    workspace.PackageTier = tier;
                    await _context.SaveChangesAsync();
                    return RedirectToPage("/WorkspaceDetail", new { joinCode = JoinCode });
                }
            }

            // Fallback to personal subscription upgrade
            userProfile.SubscriptionTier = tier;
            userProfile.SubscriptionExpires = DateTime.UtcNow.AddYears(1);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostSubmitFederationRequestAsync(string businessName, string contactPhone)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new BadRequestResult();

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile != null)
        {
            var adminNotification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userProfile.Id,
                Message = $"The Enterprise Federation registration request for '{businessName}' (Phone: {contactPhone}) has been successfully submitted and is awaiting administrator review.",
                Type = "FederationRequest",
                Link = "/workspaces",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            
            await _context.Notifications.AddAsync(adminNotification);
            await _context.SaveChangesAsync();
        }

        return new JsonResult(new { success = true });
    }
}
