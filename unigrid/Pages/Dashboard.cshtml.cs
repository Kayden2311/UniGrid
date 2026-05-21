using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System.Security.Claims;

namespace unigrid.Pages;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
public class DashboardModel : PageModel
{
    private readonly UniGridDbContext _context;

    public DashboardModel(UniGridDbContext context)
    {
        _context = context;
    }

    public List<unigrid.Models.Task> RecentTasks { get; set; } = new();
    public List<Workspace> UserWorkspaces { get; set; } = new();
    public string CurrentUserName { get; set; } = "User";
    public string UserInitials { get; set; } = "U";
    
    public int TotalTasksCount { get; set; }
    public int CompletedTasksCount { get; set; }
    public int DueSoonCount { get; set; }
    public int OverdueCount { get; set; }
    public decimal CompletionRate { get; set; }

    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync()
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return RedirectToPage("/Login");

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        
        if (userProfile != null)
        {
            CurrentUserName = userProfile.FullName;
            UserInitials = string.Concat(userProfile.FullName.Split(' ').Select(n => n[0]));
            
            ViewData["UserName"] = CurrentUserName;
            ViewData["UserInitials"] = UserInitials;

            // Fetch Workspaces
            UserWorkspaces = await _context.Workspaces
                .Include(w => w.Tasks)
                .Where(w => w.OwnerId == userProfile.Id || w.WorkspaceMembers.Any(m => m.UserId == userProfile.Id))
                .ToListAsync();
            
            ViewData["Workspaces"] = UserWorkspaces;

            // Fetch All Tasks for Stats
            var allUserTasks = await _context.Tasks
                .Include(t => t.Workspace)
                .Where(t => t.AssigneeId == userProfile.Id)
                .ToListAsync();

            TotalTasksCount = allUserTasks.Count;
            CompletedTasksCount = allUserTasks.Count(t => t.Status == 2); // 2 = Completed
            
            var today = DateTime.UtcNow;
            OverdueCount = allUserTasks.Count(t => t.Status != 2 && t.DueDate.HasValue && t.DueDate.Value < today);
            DueSoonCount = allUserTasks.Count(t => t.Status != 2 && t.DueDate.HasValue && t.DueDate.Value >= today && t.DueDate.Value <= today.AddDays(3));

            CompletionRate = TotalTasksCount > 0 ? (decimal)CompletedTasksCount / TotalTasksCount * 100 : 0;

            // Filtered tasks for the list (Recent)
            RecentTasks = allUserTasks
                .OrderByDescending(t => t.CreatedAt)
                .Take(10)
                .ToList();

            return Page();
        }

        return RedirectToPage("/Login");
    }
}
