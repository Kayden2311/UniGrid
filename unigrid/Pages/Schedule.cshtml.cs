using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;

namespace unigrid.Pages;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "2")]
public class ScheduleModel : PageModel
{
    private readonly UniGridDbContext _context;

    public ScheduleModel(UniGridDbContext context)
    {
        _context = context;
    }

    public List<unigrid.Models.Task> WorkspaceTasks { get; set; } = new();
    public List<PersonalSchedule> PersonalEvents { get; set; } = new();

    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync()
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return RedirectToPage("/Login");

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);

        if (userProfile != null)
        {
            ViewData["UserName"] = userProfile.FullName;
            ViewData["UserInitials"] = string.Concat(userProfile.FullName.Split(' ').Select(n => n[0]));

            // Fetch Workspaces for sidebar
            var userWorkspaces = await _context.Workspaces
                .Where(w => w.OwnerId == userProfile.Id || w.WorkspaceMembers.Any(m => m.UserId == userProfile.Id))
                .ToListAsync();
            ViewData["Workspaces"] = userWorkspaces;

            // Fetch Tasks
            WorkspaceTasks = await _context.Tasks
                .Include(t => t.Workspace)
                .Where(t => t.AssigneeId == userProfile.Id)
                .ToListAsync();

            // Fetch Personal Schedule
            PersonalEvents = await _context.PersonalSchedules
                .Where(p => p.UserId == userProfile.Id)
                .ToListAsync();

            return Page();
        }

        return RedirectToPage("/Login");
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostCreateEventAsync(string title, string description, DateTime startTime, DateTime endTime)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new JsonResult(new { success = false, message = "Not authenticated" });

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile == null) return new JsonResult(new { success = false, message = "User not found" });

        var newEvent = new PersonalSchedule
        {
            Id = Guid.NewGuid(),
            UserId = userProfile.Id,
            Title = title,
            Description = description,
            StartTime = startTime.ToUniversalTime(),
            EndTime = endTime.ToUniversalTime(),
            CreatedAt = DateTime.UtcNow
        };

        _context.PersonalSchedules.Add(newEvent);
        await _context.SaveChangesAsync();

        return new JsonResult(new
        {
            success = true,
            eventItem = new
            {
                id = newEvent.Id,
                title = newEvent.Title,
                description = newEvent.Description ?? "",
                startTime = newEvent.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                endTime = newEvent.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }
        });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostEditEventAsync(Guid eventId, string title, string description, DateTime startTime, DateTime endTime)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new JsonResult(new { success = false, message = "Not authenticated" });

        var eventItem = await _context.PersonalSchedules.FirstOrDefaultAsync(p => p.Id == eventId);
        if (eventItem != null)
        {
            eventItem.Title = title;
            eventItem.Description = description;
            eventItem.StartTime = startTime.ToUniversalTime();
            eventItem.EndTime = endTime.ToUniversalTime();

            await _context.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                eventItem = new
                {
                    id = eventItem.Id,
                    title = eventItem.Title,
                    description = eventItem.Description ?? "",
                    startTime = eventItem.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    endTime = eventItem.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }
            });
        }

        return new JsonResult(new { success = false, message = "Event not found" });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostDeleteEventAsync(Guid eventId)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new JsonResult(new { success = false, message = "Not authenticated" });

        var eventItem = await _context.PersonalSchedules.FirstOrDefaultAsync(p => p.Id == eventId);
        if (eventItem != null)
        {
            _context.PersonalSchedules.Remove(eventItem);
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        return new JsonResult(new { success = false, message = "Event not found" });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateEventTimeAsync(Guid eventId, DateTime startTime, DateTime endTime)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new JsonResult(new { success = false, message = "Not authenticated" });

        var eventItem = await _context.PersonalSchedules.FirstOrDefaultAsync(p => p.Id == eventId);
        if (eventItem != null)
        {
            eventItem.StartTime = startTime.ToUniversalTime();
            eventItem.EndTime = endTime.ToUniversalTime();
            await _context.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                eventItem = new
                {
                    id = eventItem.Id,
                    title = eventItem.Title,
                    description = eventItem.Description ?? "",
                    startTime = eventItem.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    endTime = eventItem.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }
            });
        }

        return new JsonResult(new { success = false, message = "Event not found" });
    }
}
