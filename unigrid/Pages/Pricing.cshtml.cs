using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using System.Security.Claims;

namespace unigrid.Pages;

public class PricingModel : PageModel
{
    private readonly UniGridDbContext _context;

    public PricingModel(UniGridDbContext context)
    {
        _context = context;
    }

    public string CurrentPlan { get; set; } = "None";

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
            userProfile.SubscriptionTier = tier;
            userProfile.SubscriptionExpires = DateTime.UtcNow.AddYears(1);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
