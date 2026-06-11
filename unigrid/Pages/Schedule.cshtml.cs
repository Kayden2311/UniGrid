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

        if (userProfile == null)
        {
            var accountRecord = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
            if (accountRecord != null)
            {
                var parts = accountRecord.Email.Split('@')[0].Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                var fullNameParts = parts.Select(n => n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1).ToLower() : string.Empty);
                var parsedName = string.Join(" ", fullNameParts);
                if (string.IsNullOrWhiteSpace(parsedName)) parsedName = "User";

                userProfile = new User
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    FullName = parsedName,
                    SubscriptionTier = "Free"
                };
                await _context.Users.AddAsync(userProfile);
                await _context.SaveChangesAsync();
            }
        }

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
                .Where(p => !p.IsDisabled && p.UserId == userProfile.Id)
                .ToListAsync();

            return Page();
        }

        return RedirectToPage("/Login");
    }

    private async System.Threading.Tasks.Task<bool> HasConflictAsync(Guid userId, Guid ignoreEventId, DateTime startTime, DateTime endTime)
    {
        var utcStart = startTime.ToUniversalTime();
        var utcEnd = endTime.ToUniversalTime();
        return await _context.PersonalSchedules.AnyAsync(p =>
            p.UserId == userId &&
            p.Id != ignoreEventId &&
            !p.IsDisabled &&
            p.StartTime < utcEnd &&
            p.EndTime > utcStart
        );
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostCreateEventAsync(string title, string description, DateTime startTime, DateTime endTime, string? timeZone = "UTC")
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new JsonResult(new { success = false, message = "Not authenticated" });

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile == null) return new JsonResult(new { success = false, message = "User not found" });

        if (await HasConflictAsync(userProfile.Id, Guid.Empty, startTime, endTime))
        {
            return new JsonResult(new { success = false, message = "Scheduling conflict! This time slot overlaps with another event on your calendar." });
        }

        var newEvent = new PersonalSchedule
        {
            Id = Guid.NewGuid(),
            UserId = userProfile.Id,
            Title = Helpers.InputSanitizer.SanitizeInput(title),
            Description = Helpers.InputSanitizer.SanitizeInput(description),
            StartTime = startTime.ToUniversalTime(),
            EndTime = endTime.ToUniversalTime(),
            TimeZone = string.IsNullOrEmpty(timeZone) ? "UTC" : timeZone,
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
                endTime = newEvent.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                timeZone = newEvent.TimeZone
            }
        });
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostEditEventAsync(Guid eventId, string title, string description, DateTime startTime, DateTime endTime, string? timeZone = "UTC")
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new JsonResult(new { success = false, message = "Not authenticated" });

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile == null) return new JsonResult(new { success = false, message = "User not found" });

        if (await HasConflictAsync(userProfile.Id, eventId, startTime, endTime))
        {
            return new JsonResult(new { success = false, message = "Scheduling conflict! This time slot overlaps with another event on your calendar." });
        }

        var eventItem = await _context.PersonalSchedules.FirstOrDefaultAsync(p => p.Id == eventId);
        if (eventItem != null)
        {
            eventItem.Title = Helpers.InputSanitizer.SanitizeInput(title);
            eventItem.Description = Helpers.InputSanitizer.SanitizeInput(description);
            eventItem.StartTime = startTime.ToUniversalTime();
            eventItem.EndTime = endTime.ToUniversalTime();
            eventItem.TimeZone = string.IsNullOrEmpty(timeZone) ? "UTC" : timeZone;

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
                    endTime = eventItem.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    timeZone = eventItem.TimeZone
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

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile == null) return new JsonResult(new { success = false, message = "User not found" });

        if (await HasConflictAsync(userProfile.Id, eventId, startTime, endTime))
        {
            return new JsonResult(new { success = false, message = "Scheduling conflict! This time slot overlaps with another event on your calendar." });
        }

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

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpdateTaskTimeAsync(Guid taskId, DateTime startTime, DateTime endTime, string? timeZone = "UTC")
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new JsonResult(new { success = false, message = "Not authenticated" });

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile == null) return new JsonResult(new { success = false, message = "User not found" });

        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task == null) return new JsonResult(new { success = false, message = "Task not found" });

        if (task.DueDate.HasValue)
        {
            if (endTime.ToUniversalTime() > task.DueDate.Value.ToUniversalTime())
            {
                return new JsonResult(new { success = false, message = $"Scheduling conflict! You cannot schedule this task past its due date ({task.DueDate.Value.ToString("yyyy-MM-dd HH:mm")})." });
            }
        }

        var personalSchedule = await _context.PersonalSchedules.FirstOrDefaultAsync(p => !p.IsDisabled && p.UserId == userProfile.Id && p.TaskId == taskId);
        var ignoreId = personalSchedule?.Id ?? Guid.Empty;

        if (await HasConflictAsync(userProfile.Id, ignoreId, startTime, endTime))
        {
            return new JsonResult(new { success = false, message = "Scheduling conflict! This time slot overlaps with another event on your calendar." });
        }

        if (personalSchedule != null)
        {
            personalSchedule.StartTime = startTime.ToUniversalTime();
            personalSchedule.EndTime = endTime.ToUniversalTime();
            personalSchedule.TimeZone = string.IsNullOrEmpty(timeZone) ? "UTC" : timeZone;
        }
        else
        {
            personalSchedule = new PersonalSchedule
            {
                Id = Guid.NewGuid(),
                UserId = userProfile.Id,
                TaskId = taskId,
                Title = task.Title,
                Description = task.Description,
                StartTime = startTime.ToUniversalTime(),
                EndTime = endTime.ToUniversalTime(),
                TimeZone = string.IsNullOrEmpty(timeZone) ? "UTC" : timeZone,
                CreatedAt = DateTime.UtcNow
            };
            _context.PersonalSchedules.Add(personalSchedule);
        }

        await _context.SaveChangesAsync();

        return new JsonResult(new
        {
            success = true,
            eventItem = new
            {
                id = personalSchedule.Id,
                title = personalSchedule.Title,
                description = personalSchedule.Description ?? "",
                startTime = personalSchedule.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                endTime = personalSchedule.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                taskId = personalSchedule.TaskId,
                timeZone = personalSchedule.TimeZone
            }
        });
    }
}
