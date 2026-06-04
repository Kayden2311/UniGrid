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
                .Include(i => i.Federation)
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

            string targetName = "";
            string? returnJoinCode = "";

            if (invitation.WorkspaceId.HasValue)
            {
                var workspaceIdVal = invitation.WorkspaceId.Value;
                targetName = invitation.Workspace?.Name ?? "Workspace";
                returnJoinCode = invitation.Workspace?.JoinCode;

                // Create member link
                var alreadyMember = await _context.WorkspaceMembers
                    .AnyAsync(wm => wm.WorkspaceId == workspaceIdVal && wm.UserId == user.Id);

                if (!alreadyMember)
                {
                    // Verify plan member limits
                    int currentMembersCount = await _context.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == workspaceIdVal);
                    int maxMembersAllowed = 5; // Default for Free/Personal
                    string tier = invitation.Workspace?.PackageTier ?? "Free";
                    if (tier == "Pro") maxMembersAllowed = 10;
                    else if (tier == "ProPlus") maxMembersAllowed = 15;
                    else if (tier == "Business") maxMembersAllowed = 30;

                    if (currentMembersCount >= maxMembersAllowed)
                    {
                        return BadRequest(new { message = $"Cannot accept. This workspace has reached the member limit ({maxMembersAllowed}) of the {tier} tier." });
                    }

                    var member = new WorkspaceMember
                    {
                        WorkspaceId = workspaceIdVal,
                        UserId = user.Id,
                        Role = invitation.Role,
                        DisplayRole = invitation.DisplayRole,
                        JoinedAt = DateTime.UtcNow
                    };
                    await _context.WorkspaceMembers.AddAsync(member);
                }

                // Add notification to inviter
                var inviterNotification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = invitation.InviterId,
                    Message = $"{user.FullName} has accepted the invitation to join Workspace '{targetName}' as '{invitation.Role}'.",
                    Type = "InvitationAccepted",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    RelatedId = workspaceIdVal
                };
                await _context.Notifications.AddAsync(inviterNotification);

                // Clear cache keys
                _cache.Remove($"WorkspaceMembers_{workspaceIdVal}");
            }
            else if (invitation.FederationId.HasValue)
            {
                var federationIdVal = invitation.FederationId.Value;
                targetName = invitation.Federation?.Name ?? "Federation";
                returnJoinCode = invitation.Federation?.JoinCode;

                // Create federation member link
                var alreadyFedMember = await _context.WorkspaceFederationMembers
                    .AnyAsync(wfm => wfm.FederationId == federationIdVal && wfm.UserId == user.Id);

                if (!alreadyFedMember)
                {
                    var fedMember = new WorkspaceFederationMember
                    {
                        FederationId = federationIdVal,
                        UserId = user.Id,
                        PersonalWorkspaceId = null, // federation invitation joins directly
                        JoinedAt = DateTime.UtcNow,
                        Role = invitation.Role ?? "Member",
                        Status = "Active"
                    };
                    await _context.WorkspaceFederationMembers.AddAsync(fedMember);
                }

                // Add notification to inviter
                var inviterNotification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = invitation.InviterId,
                    Message = $"{user.FullName} has accepted the invitation to join Federation '{targetName}' as '{invitation.Role}'.",
                    Type = "InvitationAccepted",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    RelatedId = federationIdVal
                };
                await _context.Notifications.AddAsync(inviterNotification);
            }

            // Update invitation status
            invitation.Status = "Accepted";
            await _context.SaveChangesAsync();

            // Clear general workspaces cache keys
            _cache.Remove($"UserWorkspaces_{user.Id}");
            _cache.Remove($"UserWorkspaces_{invitation.InviterId}");

            return Ok(new { success = true, joinCode = returnJoinCode });
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
