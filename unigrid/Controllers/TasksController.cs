using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using unigrid.Data.Repositories;
using unigrid.Services;

namespace unigrid.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "2")] // Restrict to authenticated Users
    public class TasksController : ControllerBase
    {
        private readonly ITaskRepository _taskRepo;
        private readonly IMemberRepository _memberRepo;
        private readonly ITaskService _taskService;
        private readonly ILogger<TasksController> _logger;

        public TasksController(
            ITaskRepository taskRepo,
            IMemberRepository memberRepo,
            ITaskService taskService,
            ILogger<TasksController> logger)
        {
            _taskRepo = taskRepo;
            _memberRepo = memberRepo;
            _taskService = taskService;
            _logger = logger;
        }

        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
        {
            _logger.LogInformation("REST API: UpdateStatus called for Task {TaskId} with Status {Status}", id, request.Status);

            var task = await _taskRepo.GetByIdAsync(id);
            if (task == null)
            {
                _logger.LogWarning("REST API: Task {TaskId} not found.", id);
                return NotFound(new { message = "Task not found." });
            }

            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim))
            {
                return Unauthorized(new { message = "You must be logged in to perform this action." });
            }

            var accountId = Guid.Parse(accountIdClaim);
            var userProfile = await _memberRepo.GetUserByAccountIdAsync(accountId);
            if (userProfile == null)
            {
                return Unauthorized(new { message = "Account information not found." });
            }

            var error = await _taskService.UpdateTaskStatusAsync(task.WorkspaceId, userProfile.Id, id, request.Status);
            if (error != null)
            {
                _logger.LogWarning("REST API: Status update blocked for Task {TaskId}. Reason: {Error}", id, error);
                
                if (error.Contains("permission") || error.Contains("only move") || error.Contains("approval"))
                {
                    return StatusCode(403, new { message = error });
                }
                return BadRequest(new { message = error });
            }

            _logger.LogInformation("REST API: Task {TaskId} status updated successfully to {Status}", id, request.Status);
            return NoContent();
        }

        [HttpPatch("{id}/counter")]
        public async Task<IActionResult> UpdateCounter(Guid id, [FromBody] UpdateCounterRequest request)
        {
            _logger.LogInformation("REST API: UpdateCounter called for Task {TaskId} with Count {Count}", id, request.CurrentCount);

            var task = await _taskRepo.GetByIdAsync(id);
            if (task == null)
            {
                _logger.LogWarning("REST API: Task {TaskId} not found.", id);
                return NotFound(new { message = "Task not found." });
            }

            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim))
            {
                return Unauthorized(new { message = "You must be logged in to perform this action." });
            }

            var accountId = Guid.Parse(accountIdClaim);
            var userProfile = await _memberRepo.GetUserByAccountIdAsync(accountId);
            if (userProfile == null)
            {
                return Unauthorized(new { message = "Account information not found." });
            }

            var error = await _taskService.UpdateTaskCounterAsync(task.WorkspaceId, userProfile.Id, id, request.CurrentCount);
            if (error != null)
            {
                _logger.LogWarning("REST API: Counter update blocked for Task {TaskId}. Reason: {Error}", id, error);
                
                if (error.Contains("permission") || error.Contains("cannot"))
                {
                    return StatusCode(403, new { message = error });
                }
                return BadRequest(new { message = error });
            }

            _logger.LogInformation("REST API: Task {TaskId} counter updated successfully to {Count}", id, request.CurrentCount);
            return NoContent();
        }
    }

    public class UpdateCounterRequest
    {
        public int CurrentCount { get; set; }
    }

    public class UpdateStatusRequest
    {
        public int Status { get; set; }
    }
}
