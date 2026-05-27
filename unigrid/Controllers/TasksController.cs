using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using unigrid.Data;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace unigrid.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "2")] // Restrict to authenticated Users
    public class TasksController : ControllerBase
    {
        private readonly UniGridDbContext _context;
        private readonly ILogger<TasksController> _logger;
        private readonly IMemoryCache _cache;

        public TasksController(UniGridDbContext context, ILogger<TasksController> _logger, IMemoryCache cache)
        {
            this._context = context;
            this._logger = _logger;
            this._cache = cache;
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

            // Retrieve current user and verify workspace permissions
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim))
            {
                return Unauthorized(new { message = "You must be logged in to perform this action." });
            }

            var accountId = Guid.Parse(accountIdClaim);
            var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (userProfile == null)
            {
                return Unauthorized(new { message = "Account information not found." });
            }

            var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == task.WorkspaceId);
            if (workspace == null)
            {
                return BadRequest(new { message = "Workspace does not exist." });
            }

            var memberRecord = await _context.WorkspaceMembers
                .FirstOrDefaultAsync(wm => wm.WorkspaceId == task.WorkspaceId && wm.UserId == userProfile.Id);

            string currentUserRole = memberRecord?.Role ?? (workspace.OwnerId == userProfile.Id ? "Owner" : "Member");

            // Enforcement of Role Governance:
            // - Owners and Managers can move any task and transition to/from any status.
            // - Normal Members can only move tasks assigned specifically to them.
            // - Normal Members cannot drag/move tasks directly to status = 3 (Done).
            if (currentUserRole != "Owner" && currentUserRole != "Manager")
            {
                if (task.AssigneeId != userProfile.Id)
                {
                    _logger.LogWarning("REST API: Member {UserId} attempted to move unassigned task {TaskId}.", userProfile.Id, id);
                    return StatusCode(403, new { message = "You can only move tasks assigned to yourself!" });
                }
                if (request.Status == 3)
                {
                    _logger.LogWarning("REST API: Member {UserId} attempted to complete task {TaskId} without management approval.", userProfile.Id, id);
                    return StatusCode(403, new { message = "Only Managers or Owners have permission to approve and complete tasks!" });
                }
                if (task.Status == 3)
                {
                    _logger.LogWarning("REST API: Member {UserId} attempted to rework completed task {TaskId}.", userProfile.Id, id);
                    return StatusCode(403, new { message = "Only Managers or Owners have permission to rework completed tasks!" });
                }
            }

            task.Status = request.Status;
            await _context.SaveChangesAsync();

            // Evict Cache
            _cache.Remove($"WorkspaceTasks_{task.WorkspaceId}");
            
            var memberUserIds = await _context.WorkspaceMembers
                .Where(wm => wm.WorkspaceId == task.WorkspaceId)
                .Select(wm => wm.UserId)
                .ToListAsync();

            foreach (var memberId in memberUserIds)
            {
                _cache.Remove($"UserWorkspaces_{memberId}");
                _cache.Remove($"UserTasks_{memberId}");
            }

            if (workspace != null)
            {
                _cache.Remove($"UserWorkspaces_{workspace.OwnerId}");
                _cache.Remove($"UserTasks_{workspace.OwnerId}");
            }

            _logger.LogInformation("REST API: Task {TaskId} status updated successfully to {Status}", id, request.Status);

            return NoContent();
        }
    }

    public class UpdateStatusRequest
    {
        public int Status { get; set; }
    }
}
