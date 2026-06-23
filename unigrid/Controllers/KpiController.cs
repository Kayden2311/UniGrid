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
    public class KpiController : ControllerBase
    {
        private readonly ITaskService _taskService;
        private readonly IMemberRepository _memberRepo;
        private readonly IWorkspaceRepository _workspaceRepo;
        private readonly ILogger<KpiController> _logger;

        public KpiController(
            ITaskService taskService,
            IMemberRepository memberRepo,
            IWorkspaceRepository workspaceRepo,
            ILogger<KpiController> logger)
        {
            _taskService = taskService;
            _memberRepo = memberRepo;
            _workspaceRepo = workspaceRepo;
            _logger = logger;
        }

        // =========================================================================
        // CATEGORY API
        // =========================================================================

        [HttpGet("workspace/{workspaceId}/categories")]
        public async Task<IActionResult> GetCategories(Guid workspaceId)
        {
            _logger.LogInformation("KpiAPI: GetCategories for workspace {WorkspaceId}", workspaceId);
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { message = "User not found." });

            var hasAccess = await IsUserInWorkspaceAsync(workspaceId, user.Id);
            if (!hasAccess) return StatusCode(403, new { message = "Access denied to this workspace." });

            var categories = await _taskService.GetWorkspaceCategoriesAsync(workspaceId);
            return Ok(categories);
        }

        [HttpPost("workspace/{workspaceId}/categories")]
        public async Task<IActionResult> CreateCategory(Guid workspaceId, [FromBody] CreateCategoryRequest request)
        {
            _logger.LogInformation("KpiAPI: CreateCategory called in workspace {WorkspaceId}", workspaceId);

            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { message = "User not found." });

            var hasAccess = await IsUserInWorkspaceAsync(workspaceId, user.Id);
            if (!hasAccess) return StatusCode(403, new { message = "Access denied to this workspace." });

            var error = await _taskService.CreateCategoryAsync(workspaceId, user.Id, request.Name, request.Description, request.ColorHex);
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            return Ok(new { message = "Category created successfully." });
        }

        [HttpPut("workspace/{workspaceId}/categories/{categoryId}")]
        public async Task<IActionResult> UpdateCategory(Guid workspaceId, Guid categoryId, [FromBody] CreateCategoryRequest request)
        {
            _logger.LogInformation("KpiAPI: UpdateCategory called for {CategoryId}", categoryId);

            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { message = "User not found." });

            var hasAccess = await IsUserInWorkspaceAsync(workspaceId, user.Id);
            if (!hasAccess) return StatusCode(403, new { message = "Access denied to this workspace." });

            var error = await _taskService.UpdateCategoryAsync(workspaceId, user.Id, categoryId, request.Name, request.Description, request.ColorHex);
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            return Ok(new { message = "Category updated successfully." });
        }

        [HttpDelete("workspace/{workspaceId}/categories/{categoryId}")]
        public async Task<IActionResult> DeleteCategory(Guid workspaceId, Guid categoryId)
        {
            _logger.LogInformation("KpiAPI: DeleteCategory called for {CategoryId}", categoryId);

            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { message = "User not found." });

            var hasAccess = await IsUserInWorkspaceAsync(workspaceId, user.Id);
            if (!hasAccess) return StatusCode(403, new { message = "Access denied to this workspace." });

            var error = await _taskService.DeleteCategoryAsync(workspaceId, user.Id, categoryId);
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            return Ok(new { message = "Category deleted successfully." });
        }

        // =========================================================================
        // KPI TARGET API
        // =========================================================================

        [HttpGet("workspace/{workspaceId}/targets")]
        public async Task<IActionResult> GetKpiTargets(Guid workspaceId)
        {
            _logger.LogInformation("KpiAPI: GetKpiTargets for workspace {WorkspaceId}", workspaceId);
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { message = "User not found." });

            var hasAccess = await IsUserInWorkspaceAsync(workspaceId, user.Id);
            if (!hasAccess) return StatusCode(403, new { message = "Access denied to this workspace." });

            var targets = await _taskService.GetWorkspaceTargetsAsync(workspaceId);
            return Ok(targets);
        }

        [HttpPost("workspace/{workspaceId}/targets")]
        public async Task<IActionResult> CreateKpiTarget(Guid workspaceId, [FromBody] CreateKpiTargetRequest request)
        {
            _logger.LogInformation("KpiAPI: CreateKpiTarget in workspace {WorkspaceId}", workspaceId);

            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { message = "User not found." });

            var hasAccess = await IsUserInWorkspaceAsync(workspaceId, user.Id);
            if (!hasAccess) return StatusCode(403, new { message = "Access denied to this workspace." });

            var error = await _taskService.CreateKpiTargetAsync(
                workspaceId, user.Id, request.UserId, request.CategoryId, request.PeriodType, request.StartDate, request.EndDate, request.TargetValue);

            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            return Ok(new { message = "KPI Target created successfully." });
        }

        [HttpDelete("workspace/{workspaceId}/targets/{targetId}")]
        public async Task<IActionResult> DeleteKpiTarget(Guid workspaceId, Guid targetId)
        {
            _logger.LogInformation("KpiAPI: DeleteKpiTarget called for target {TargetId}", targetId);

            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { message = "User not found." });

            var hasAccess = await IsUserInWorkspaceAsync(workspaceId, user.Id);
            if (!hasAccess) return StatusCode(403, new { message = "Access denied to this workspace." });

            var error = await _taskService.DeleteKpiTargetAsync(workspaceId, user.Id, targetId);
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            return Ok(new { message = "KPI Target deleted successfully." });
        }

        // =========================================================================
        // REPORT API
        // =========================================================================

        [HttpGet("workspace/{workspaceId}/report")]
        public async Task<IActionResult> GetKpiReport(Guid workspaceId, [FromQuery] string periodType, [FromQuery] DateTime targetDate)
        {
            _logger.LogInformation("KpiAPI: GetKpiReport for workspace {WorkspaceId}, period {Period}, targetDate {Date}", workspaceId, periodType, targetDate);
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { message = "User not found." });

            var hasAccess = await IsUserInWorkspaceAsync(workspaceId, user.Id);
            if (!hasAccess) return StatusCode(403, new { message = "Access denied to this workspace." });

            var report = await _taskService.GetKpiReportAsync(workspaceId, periodType, targetDate);
            return Ok(report);
        }

        // =========================================================================
        // HELPERS
        // =========================================================================

        private async Task<unigrid.Models.User?> GetCurrentUserAsync()
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return null;

            var accountId = Guid.Parse(accountIdClaim);
            return await _memberRepo.GetUserByAccountIdAsync(accountId);
        }

        private async Task<bool> IsUserInWorkspaceAsync(Guid workspaceId, Guid userId)
        {
            var members = await _memberRepo.GetWorkspaceMembersAsync(workspaceId);
            if (members.Any(m => m.UserId == userId)) return true;

            var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
            return workspace != null && workspace.OwnerId == userId;
        }
    }

    public class CreateCategoryRequest
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string ColorHex { get; set; } = "#3B82F6";
    }

    public class CreateKpiTargetRequest
    {
        public Guid UserId { get; set; }
        public Guid CategoryId { get; set; }
        public string PeriodType { get; set; } = null!; // "Daily", "Weekly", "Monthly"
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TargetValue { get; set; }
    }
}
