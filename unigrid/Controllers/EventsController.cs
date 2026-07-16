using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using unigrid.Data;
using unigrid.Models;

namespace unigrid.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "2")] // Restrict to authenticated Users
    public class EventsController : ControllerBase
    {
        private readonly UniGridDbContext _context;
        private readonly ILogger<EventsController> _logger;

        public EventsController(UniGridDbContext context, ILogger<EventsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("snapshot")]
        public async Task<IActionResult> GetSnapshot()
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (!Guid.TryParse(accountIdClaim, out var accountId))
            {
                return Unauthorized();
            }

            var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (userProfile == null)
            {
                return Unauthorized();
            }

            var eventEntities = await _context.PersonalSchedules
                .Where(e => !e.IsDisabled && e.UserId == userProfile.Id)
                .ToListAsync();
            var events = eventEntities.Select(e => new
            {
                id = e.Id,
                title = e.Title,
                description = e.Description ?? string.Empty,
                startTime = e.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                endTime = e.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                taskId = e.TaskId,
                timeZone = e.TimeZone ?? "UTC"
            });

            var taskEntities = await _context.Tasks
                .Include(t => t.Workspace)
                .Where(t => !t.IsDisabled && t.AssigneeId == userProfile.Id)
                .ToListAsync();
            var tasks = taskEntities.Select(t =>
            {
                var dueDate = t.DueDate;
                if (dueDate.HasValue && dueDate.Value.TimeOfDay == TimeSpan.Zero)
                {
                    dueDate = dueDate.Value.Date.AddHours(23).AddMinutes(50);
                }
                return new
                {
                    id = t.Id,
                    title = t.Title,
                    description = t.Description ?? string.Empty,
                    workspaceName = t.Workspace?.Name ?? "Workspace",
                    workspaceJoinCode = t.Workspace?.JoinCode ?? string.Empty,
                    dueDate = dueDate?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    priority = t.Priority == 3 ? "high" : (t.Priority == 2 ? "medium" : "low")
                };
            });

            return Ok(new { events, tasks });
        }

        [HttpPatch("{id}/time")]
        public async Task<IActionResult> UpdateEventTime(Guid id, [FromBody] UpdateEventTimeRequest request)
        {
            _logger.LogInformation("REST API: UpdateEventTime called for Event {EventId} with Start {Start} and End {End}", id, request.StartTime, request.EndTime);

            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim))
            {
                return Unauthorized(new { message = "You must be logged in to perform this action." });
            }

            var accountId = Guid.Parse(accountIdClaim);
            var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (userProfile == null)
            {
                return Unauthorized(new { message = "User profile not found." });
            }

            var ev = await _context.PersonalSchedules.FirstOrDefaultAsync(e => e.Id == id);
            if (ev == null)
            {
                _logger.LogWarning("REST API: Event {EventId} not found.", id);
                return NotFound(new { message = "Event not found." });
            }

            if (ev.UserId != userProfile.Id)
            {
                _logger.LogWarning("REST API: Unauthorized event modification attempt by User {UserId} on Event {EventId}", userProfile.Id, id);
                return StatusCode(403, new { message = "You do not have permission to modify this event." });
            }

            // Convert to UTC to match seeding and database standards
            ev.StartTime = request.StartTime.ToUniversalTime();
            ev.EndTime = request.EndTime.ToUniversalTime();
            await _context.SaveChangesAsync();

            _logger.LogInformation("REST API: Event {EventId} time updated successfully.", id);

            return NoContent();
        }
    }

    public class UpdateEventTimeRequest
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
}
