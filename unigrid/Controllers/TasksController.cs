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
    public class TasksController : ControllerBase
    {
        private readonly UniGridDbContext _context;
        private readonly ILogger<TasksController> _logger;

        public TasksController(UniGridDbContext context, ILogger<TasksController> _logger)
        {
            this._context = context;
            this._logger = _logger;
        }

        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
        {
            _logger.LogInformation("REST API: UpdateStatus called for Task {TaskId} with Status {Status}", id, request.Status);

            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null)
            {
                _logger.LogWarning("REST API: Task {TaskId} not found.", id);
                return NotFound(new { message = "Task not found." });
            }

            task.Status = request.Status;
            await _context.SaveChangesAsync();

            _logger.LogInformation("REST API: Task {TaskId} status updated successfully to {Status}", id, request.Status);

            return NoContent();
        }
    }

    public class UpdateStatusRequest
    {
        public int Status { get; set; }
    }
}
