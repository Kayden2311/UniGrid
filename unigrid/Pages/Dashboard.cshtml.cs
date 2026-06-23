using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace unigrid.Pages;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
public class DashboardModel : PageModel
{
    private readonly UniGridDbContext _context;
    private readonly IMemoryCache _cache;

    public DashboardModel(UniGridDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
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
        
        // Cache user profile
        var userProfile = await _cache.GetOrCreateAsync($"User_{accountId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        });
        
        if (userProfile == null)
        {
            return RedirectToPage("/Profile");
        }
        
        if (userProfile != null)
        {
            CurrentUserName = userProfile.FullName;
            UserInitials = string.Concat(userProfile.FullName.Split(' ').Select(n => n[0]));
            
            ViewData["UserName"] = CurrentUserName;
            ViewData["UserInitials"] = UserInitials;

            // Fetch Workspaces (Cache)
            UserWorkspaces = await _cache.GetOrCreateAsync($"UserWorkspaces_{userProfile.Id}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Workspaces
                    .Include(w => w.Tasks)
                    .Where(w => !w.IsDisabled && (w.OwnerId == userProfile.Id || w.WorkspaceMembers.Any(m => !m.IsDisabled && m.UserId == userProfile.Id)))
                    .ToListAsync();
            });
            
            ViewData["Workspaces"] = UserWorkspaces;

            // Fetch All Tasks for Stats (Cache)
            var allUserTasks = await _cache.GetOrCreateAsync($"UserTasks_{userProfile.Id}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _context.Tasks
                    .Include(t => t.Workspace)
                    .Where(t => t.AssigneeId == userProfile.Id)
                    .ToListAsync();
            });

            TotalTasksCount = allUserTasks.Count;
            CompletedTasksCount = allUserTasks.Count(t => t.Status == 3); // 3 = Done
            
            var today = DateTime.UtcNow;
            OverdueCount = allUserTasks.Count(t => t.Status != 3 && t.DueDate.HasValue && t.DueDate.Value < today);
            DueSoonCount = allUserTasks.Count(t => t.Status != 3 && t.DueDate.HasValue && t.DueDate.Value >= today && t.DueDate.Value <= today.AddDays(3));

            // Monthly Completion Rate specifically using tasks due within the active calendar month
            var currentMonthTasks = allUserTasks.Where(t => t.DueDate.HasValue && t.DueDate.Value.Month == today.Month && t.DueDate.Value.Year == today.Year).ToList();
            var currentMonthTotal = currentMonthTasks.Count;
            var currentMonthCompleted = currentMonthTasks.Count(t => t.Status == 3);
            CompletionRate = currentMonthTotal > 0 ? (decimal)currentMonthCompleted / currentMonthTotal * 100 : 0;

            // Filtered tasks for the list (All user tasks)
            RecentTasks = allUserTasks
                .OrderByDescending(t => t.CreatedAt)
                .ToList();

            return Page();
        }

        return RedirectToPage("/Login");
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostToggleTaskAsync(Guid taskId)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task == null) return NotFound();

        // Toggle status between 0 (Todo) and 3 (Done)
        task.Status = task.Status == 3 ? 0 : 3;
        await _context.SaveChangesAsync();

        // Evict affected cache entries
        _cache.Remove($"WorkspaceTasks_{task.WorkspaceId}");
        if (task.AssigneeId.HasValue)
        {
            _cache.Remove($"UserTasks_{task.AssigneeId.Value}");
            _cache.Remove($"UserWorkspaces_{task.AssigneeId.Value}");
        }

        return new JsonResult(new { success = true, newStatus = task.Status });
    }
}
