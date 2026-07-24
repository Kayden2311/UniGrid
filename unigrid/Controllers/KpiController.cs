using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using unigrid.Data;
using unigrid.Data.Repositories;
using unigrid.Services;
using unigrid.Models;

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
        private readonly UniGridDbContext _context;

        public KpiController(
            ITaskService taskService,
            IMemberRepository memberRepo,
            IWorkspaceRepository workspaceRepo,
            UniGridDbContext context,
            ILogger<KpiController> logger)
        {
            _taskService = taskService;
            _memberRepo = memberRepo;
            _workspaceRepo = workspaceRepo;
            _context = context;
            _logger = logger;
        }

        // KPI verification requests are server-side so requesters and managers
        // see the same pending queue across accounts, browsers, and devices.
        [HttpGet("requests/workspace/{workspaceId}")]
        public async Task<IActionResult> GetWorkspaceKpiRequests(Guid workspaceId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (!await IsUserInWorkspaceAsync(workspaceId, user.Id)) return Forbid();

            return Ok((await LoadRequestsAsync(WorkspaceRequestKey(workspaceId)))
                .Where(r => r.Status == "pending"));
        }

        [HttpPost("requests/workspace/{workspaceId}")]
        public async Task<IActionResult> CreateWorkspaceKpiRequest(Guid workspaceId, [FromBody] KpiVerificationRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (!await IsUserInWorkspaceAsync(workspaceId, user.Id)) return Forbid();
            if (request.UserId != user.Id) return Forbid();

            return await CreateRequestAsync(WorkspaceRequestKey(workspaceId), request, user);
        }

        [HttpDelete("requests/workspace/{workspaceId}/{requestId}")]
        public async Task<IActionResult> ResolveWorkspaceKpiRequest(Guid workspaceId, string requestId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (!await CanManageWorkspaceKpiAsync(workspaceId, user.Id)) return Forbid();

            return await RemoveRequestAsync(WorkspaceRequestKey(workspaceId), requestId);
        }

        [HttpGet("requests/federation/{federationId}")]
        public async Task<IActionResult> GetFederationKpiRequests(Guid federationId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (!await IsUserInFederationAsync(federationId, user.Id)) return Forbid();

            return Ok((await LoadRequestsAsync(FederationRequestKey(federationId)))
                .Where(r => r.Status == "pending"));
        }

        [HttpPost("requests/federation/{federationId}")]
        public async Task<IActionResult> CreateFederationKpiRequest(Guid federationId, [FromBody] KpiVerificationRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (!await IsUserInFederationAsync(federationId, user.Id)) return Forbid();
            if (request.UserId != user.Id) return Forbid();

            return await CreateRequestAsync(FederationRequestKey(federationId), request, user);
        }

        [HttpDelete("requests/federation/{federationId}/{requestId}")]
        public async Task<IActionResult> ResolveFederationKpiRequest(Guid federationId, string requestId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (!await CanManageFederationKpiAsync(federationId, user.Id)) return Forbid();

            return await RemoveRequestAsync(FederationRequestKey(federationId), requestId);
        }

        private async Task<IActionResult> CreateRequestAsync(string key, KpiVerificationRequest request, unigrid.Models.User user)
        {
            if (string.IsNullOrWhiteSpace(request.CategoryId) || !new[] { "Daily", "Weekly", "Monthly" }.Contains(request.PeriodType) ||
                !new[] { "increment", "complete" }.Contains(request.RequestType))
            {
                return BadRequest(new { message = "Invalid KPI verification request." });
            }

            var requests = await LoadRequestsAsync(key);
            if (requests.Any(r => r.Status == "pending" && r.UserId == user.Id && r.CategoryId == request.CategoryId && r.PeriodType == request.PeriodType))
            {
                return Conflict(new { message = "A pending request already exists for this KPI." });
            }

            request.Id = Guid.NewGuid().ToString("N");
            request.UserId = user.Id;
            request.UserName = user.FullName;
            request.IncrementValue = Math.Max(1, request.IncrementValue);
            request.CreatedAt = DateTime.UtcNow;
            request.Timestamp = "Just now";
            request.Status = "pending";
            requests.Add(request);
            await SaveRequestsAsync(key, requests);
            return Ok(request);
        }

        private async Task<IActionResult> RemoveRequestAsync(string key, string requestId)
        {
            var requests = await LoadRequestsAsync(key);
            var removed = requests.RemoveAll(r => r.Id == requestId);
            if (removed == 0) return NotFound(new { message = "KPI request not found." });
            await SaveRequestsAsync(key, requests);
            return Ok(new { success = true });
        }

        private async System.Threading.Tasks.Task<List<KpiVerificationRequest>> LoadRequestsAsync(string key)
        {
            var json = await _context.SystemSettings.AsNoTracking()
                .Where(s => s.SettingKey == key).Select(s => s.SettingValue).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return JsonSerializer.Deserialize<List<KpiVerificationRequest>>(json) ?? new(); }
            catch { return new(); }
        }

        private async System.Threading.Tasks.Task SaveRequestsAsync(string key, List<KpiVerificationRequest> requests)
        {
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key);
            if (setting == null)
            {
                setting = new SystemSetting { SettingKey = key, CreatedAt = DateTime.UtcNow };
                await _context.SystemSettings.AddAsync(setting);
            }
            setting.SettingValue = JsonSerializer.Serialize(requests);
            setting.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private async Task<bool> CanManageWorkspaceKpiAsync(Guid workspaceId, Guid userId)
        {
            var workspace = await _context.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => !w.IsDisabled && w.Id == workspaceId);
            if (workspace?.OwnerId == userId) return true;
            return await _context.WorkspaceMembers.AnyAsync(m => !m.IsDisabled && m.WorkspaceId == workspaceId && m.UserId == userId &&
                (m.Role == "Manager" || m.Role == "Vice Manager"));
        }

        private async Task<bool> IsUserInFederationAsync(Guid federationId, Guid userId) =>
            await _context.WorkspaceFederations.AnyAsync(f => !f.IsDisabled && f.Id == federationId && f.OwnerId == userId) ||
            await _context.WorkspaceFederationMembers.AnyAsync(m => !m.IsDisabled && m.Status == "Active" && m.FederationId == federationId && m.UserId == userId);

        private async Task<bool> CanManageFederationKpiAsync(Guid federationId, Guid userId)
        {
            if (await _context.WorkspaceFederations.AnyAsync(f => !f.IsDisabled && f.Id == federationId && f.OwnerId == userId)) return true;
            return await _context.WorkspaceFederationMembers.AnyAsync(m => !m.IsDisabled && m.Status == "Active" && m.FederationId == federationId &&
                m.UserId == userId && (m.Role == "HeadPresident" || m.Role == "DepartmentManager"));
        }

        private static string WorkspaceRequestKey(Guid id) => $"KpiRequests:Workspace:{id:N}";
        private static string FederationRequestKey(Guid id) => $"KpiRequests:Federation:{id:N}";

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

            var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
            if (workspace == null) return NotFound(new { message = "Workspace not found." });
            var planSetting = AdminSettings.GetPlanSetting(workspace.PackageTier);
            if (!planSetting.HasAdvancedAnalytics)
            {
                return StatusCode(403, new { message = "Advanced KPI target analytics is not supported on your workspace plan." });
            }

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

            var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
            if (workspace == null) return NotFound(new { message = "Workspace not found." });
            var planSetting = AdminSettings.GetPlanSetting(workspace.PackageTier);
            if (!planSetting.HasAdvancedAnalytics)
            {
                return StatusCode(403, new { message = "Advanced KPI target analytics is not supported on your workspace plan." });
            }

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

            var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
            if (workspace == null) return NotFound(new { message = "Workspace not found." });
            var planSetting = AdminSettings.GetPlanSetting(workspace.PackageTier);
            if (!planSetting.HasAdvancedAnalytics)
            {
                return StatusCode(403, new { message = "Advanced KPI target analytics is not supported on your workspace plan." });
            }

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

            var workspace = await _workspaceRepo.GetByIdAsync(workspaceId);
            if (workspace == null) return NotFound(new { message = "Workspace not found." });
            var planSetting = AdminSettings.GetPlanSetting(workspace.PackageTier);
            if (!planSetting.HasAdvancedAnalytics)
            {
                return StatusCode(403, new { message = "Advanced KPI target analytics is not supported on your workspace plan." });
            }

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

    public class KpiVerificationRequest
    {
        public string Id { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string PeriodType { get; set; } = "Weekly";
        public string RequestType { get; set; } = "increment";
        public int IncrementValue { get; set; } = 1;
        public string Timestamp { get; set; } = string.Empty;
        public string Status { get; set; } = "pending";
        public DateTime CreatedAt { get; set; }
    }
}
