using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;

namespace unigrid.Pages;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
public class WorkspacesModel : PageModel
{
    private readonly UniGridDbContext _context;

    public WorkspacesModel(UniGridDbContext context)
    {
        _context = context;
    }

    public List<Workspace> UserWorkspaces { get; set; } = new();

    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync()
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;

        if (!string.IsNullOrEmpty(accountIdClaim))
        {
            var accountId = Guid.Parse(accountIdClaim);
            var profile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            
            if (profile != null)
            {
                var user = await _context.Users.FindAsync(profile.Id);
                ViewData["UserName"] = user.FullName;
                ViewData["UserInitials"] = string.Concat(user.FullName.Split(' ').Select(n => n[0]));

                // Fetch Workspaces owned by or joined by the user
                UserWorkspaces = await _context.Workspaces
                    .Include(w => w.WorkspaceMembers)
                    .ThenInclude(m => m.User)
                    .Where(w => w.OwnerId == profile.Id || w.WorkspaceMembers.Any(m => m.UserId == profile.Id))
                    .ToListAsync();

                return Page();
            }
        }

        return RedirectToPage("/Login");
    }
}
