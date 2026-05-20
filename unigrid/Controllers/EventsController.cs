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

        [HttpPatch("{id}/time")]
        public async Task<IActionResult> UpdateEventTime(Guid id, [FromBody] UpdateEventTimeRequest request)
        {
            _logger.LogInformation("REST API: UpdateEventTime called for Event {EventId} with Start {Start} and End {End}", id, request.StartTime, request.EndTime);

            var ev = await _context.PersonalSchedules.FirstOrDefaultAsync(e => e.Id == id);
            if (ev == null)
            {
                _logger.LogWarning("REST API: Event {EventId} not found.", id);
                return NotFound(new { message = "Event not found." });
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
