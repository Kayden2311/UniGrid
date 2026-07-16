using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using unigrid.Models.AI;
using unigrid.Services;
using Microsoft.AspNetCore.Http;
using unigrid.Data;
using Microsoft.EntityFrameworkCore;

namespace unigrid.Controllers
{
    [ApiController]
    [Route("api/assistant")]
    [Authorize] // require authenticated user
    public class AssistantController : ControllerBase
    {
        private readonly IAIAssistantService _assistantService;
        private readonly IHttpContextAccessor _ctx;
        private readonly UniGridDbContext _db;

        public AssistantController(IAIAssistantService assistantService, IHttpContextAccessor ctx, UniGridDbContext db)
        {
            _assistantService = assistantService;
            _ctx = ctx;
            _db = db;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] AssistantRequest req)
        {
            // Resolve platform account id from claims
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim) || !Guid.TryParse(accountIdClaim, out var accountId))
            {
                return Unauthorized();
            }

            // Map AccountId -> Users.Id (the PersonalSchedules.UserId FK expects Users.Id)
            var user = await _db.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (user == null)
            {
                return Unauthorized();
            }

            // Forward history to the assistant service so Python receives conversation turns
            var response = await _assistantService.AskAsync(user.Id, req.Message ?? string.Empty, req.History);
            return Ok(response);
        }
    }
}
