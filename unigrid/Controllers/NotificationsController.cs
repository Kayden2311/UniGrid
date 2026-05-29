using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using unigrid.Data;
using unigrid.Models;

namespace unigrid.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "2")] // Restrict to authenticated Users
    public class NotificationsController : ControllerBase
    {
        private readonly UniGridDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(UniGridDbContext context, IMemoryCache cache, ILogger<NotificationsController> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        [HttpPost("mark-read/{id}")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return Unauthorized();
            var accountId = Guid.Parse(accountIdClaim);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (user == null) return Unauthorized();

            var notif = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == user.Id);
            if (notif == null)
            {
                return NotFound(new { message = "Notification not found." });
            }

            notif.IsRead = true;
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [HttpPost("mark-all-read")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return Unauthorized();
            var accountId = Guid.Parse(accountIdClaim);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (user == null) return Unauthorized();

            var unread = await _context.Notifications.Where(n => n.UserId == user.Id && !n.IsRead).ToListAsync();
            foreach (var n in unread)
            {
                n.IsRead = true;
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [HttpPost("invitations/{id}/accept")]
        public async Task<IActionResult> AcceptInvitation(Guid id)
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return Unauthorized();
            var accountId = Guid.Parse(accountIdClaim);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (user == null) return Unauthorized();

            var invitation = await _context.WorkspaceInvitations
                .Include(i => i.Workspace)
                .FirstOrDefaultAsync(i => i.Id == id && i.Status == "Pending");

            if (invitation == null)
            {
                return NotFound(new { message = "Invitation not found or already processed." });
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
            var userEmail = account?.Email;

            if (userEmail == null || !userEmail.Equals(invitation.InviteeEmail, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            // Create member link
            var alreadyMember = await _context.WorkspaceMembers
                .AnyAsync(wm => wm.WorkspaceId == invitation.WorkspaceId && wm.UserId == user.Id);

            if (!alreadyMember)
            {
                // Verify plan member limits
                int currentMembersCount = await _context.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == invitation.WorkspaceId);
                int maxMembersAllowed = 5; // Default for Free/Personal
                string tier = invitation.Workspace.PackageTier ?? "Free";
                if (tier == "Pro") maxMembersAllowed = 10;
                else if (tier == "ProPlus") maxMembersAllowed = 15;
                else if (tier == "Business") maxMembersAllowed = 30;

                if (currentMembersCount >= maxMembersAllowed)
                {
                    return BadRequest(new { message = $"Không thể đồng ý. Workspace này đã đạt giới hạn thành viên ({maxMembersAllowed}) của gói {tier}." });
                }

                var member = new WorkspaceMember
                {
                    WorkspaceId = invitation.WorkspaceId,
                    UserId = user.Id,
                    Role = invitation.Role,
                    JoinedAt = DateTime.UtcNow
                };
                await _context.WorkspaceMembers.AddAsync(member);
            }

            // Update invitation status
            invitation.Status = "Accepted";

            // Add notification to inviter
            var inviterNotification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = invitation.InviterId,
                Message = $"{user.FullName} đã chấp nhận lời mời tham gia Workspace '{invitation.Workspace.Name}' với tư cách là '{invitation.Role}'.",
                Type = "InvitationAccepted",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                RelatedId = invitation.WorkspaceId
            };
            await _context.Notifications.AddAsync(inviterNotification);

            await _context.SaveChangesAsync();

            // Clear cache keys
            _cache.Remove($"WorkspaceMembers_{invitation.WorkspaceId}");
            _cache.Remove($"UserWorkspaces_{user.Id}");
            _cache.Remove($"UserWorkspaces_{invitation.InviterId}");

            return Ok(new { success = true, joinCode = invitation.Workspace.JoinCode });
        }

        [HttpPost("invitations/{id}/decline")]
        public async Task<IActionResult> DeclineInvitation(Guid id)
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return Unauthorized();
            var accountId = Guid.Parse(accountIdClaim);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (user == null) return Unauthorized();

            var invitation = await _context.WorkspaceInvitations.FirstOrDefaultAsync(i => i.Id == id && i.Status == "Pending");
            if (invitation == null)
            {
                return NotFound(new { message = "Invitation not found or already processed." });
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
            var userEmail = account?.Email;

            if (userEmail == null || !userEmail.Equals(invitation.InviteeEmail, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            invitation.Status = "Declined";
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        [HttpGet("search-by-email")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchByEmail([FromQuery] string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest(new { message = "Email required" });
            }

            var trimmedEmail = email.Trim().ToLower();
            var matchedUser = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.Account.Email.ToLower() == trimmedEmail);

            if (matchedUser != null)
            {
                return Ok(new
                {
                    exists = true,
                    fullName = matchedUser.FullName,
                    avatarUrl = matchedUser.AvatarUrl ?? ""
                });
            }

            return Ok(new { exists = false });
        }
    }
}
