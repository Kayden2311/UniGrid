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
        private readonly unigrid.Services.INotificationService _notificationService;

        public NotificationsController(UniGridDbContext context, IMemoryCache cache, ILogger<NotificationsController> logger, unigrid.Services.INotificationService notificationService)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim)) return Unauthorized();
            var accountId = Guid.Parse(accountIdClaim);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (user == null) return Unauthorized();

            var notifications = await _context.Notifications
                .Where(n => n.UserId == user.Id)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new {
                    id = n.Id,
                    userId = n.UserId,
                    message = n.Message,
                    type = n.Type,
                    link = n.Link,
                    isRead = n.IsRead,
                    createdAt = n.CreatedAt,
                    relatedId = n.RelatedId
                })
                .ToListAsync();

            return Ok(notifications);
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

                // Create member link or re-enable existing soft-deleted record
                var memberRecord = await _context.WorkspaceMembers
                    .FirstOrDefaultAsync(wm => wm.WorkspaceId == workspaceIdVal && wm.UserId == user.Id);

                if (memberRecord == null || memberRecord.IsDisabled)
                {
                    // Verify plan member limits
                    int currentMembersCount = await _context.WorkspaceMembers.CountAsync(wm => !wm.IsDisabled && wm.WorkspaceId == workspaceIdVal);
                    string tier = invitation.Workspace?.PackageTier ?? "Free";
                    if (tier.Equals("Personal", StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new { message = "Adding members is not allowed on the Personal plan." });
                    }

                    var planSetting = AdminSettings.GetPlanSetting(tier, _context);
                    int maxMembersAllowed = planSetting?.MemberLimit ?? 5;

                    if (currentMembersCount >= maxMembersAllowed)
                    {
                        return BadRequest(new { message = $"Cannot accept. This workspace has reached the member limit ({maxMembersAllowed}) of the {tier} tier." });
                    }

                    if (memberRecord == null)
                    {
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
                    else
                    {
                        memberRecord.IsDisabled = false;
                        memberRecord.Role = invitation.Role;
                        memberRecord.DisplayRole = invitation.DisplayRole;
                        memberRecord.JoinedAt = DateTime.UtcNow;
                        _context.WorkspaceMembers.Update(memberRecord);
                    }
                }

                // Clear cache keys
                _cache.Remove($"WorkspaceMembers_{workspaceIdVal}");
                _cache.Remove($"WorkspaceTasks_{workspaceIdVal}");
                _cache.Remove($"WorkspaceFiles_{workspaceIdVal}");
                _cache.Remove($"WorkspaceChatRoom_{workspaceIdVal}");
            }
            else if (invitation.FederationId.HasValue)
            {
                var federationIdVal = invitation.FederationId.Value;
                targetName = invitation.Federation?.Name ?? "Federation";
                returnJoinCode = invitation.Federation?.JoinCode;

                // Create or reactivate a federation member link.
                var fedMember = await _context.WorkspaceFederationMembers
                    .FirstOrDefaultAsync(wfm => wfm.FederationId == federationIdVal && wfm.UserId == user.Id);

                if (fedMember == null)
                {
                    fedMember = new WorkspaceFederationMember
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
                else
                {
                    fedMember.IsDisabled = false;
                    fedMember.JoinedAt = DateTime.UtcNow;
                    fedMember.Role = invitation.Role ?? "Member";
                    fedMember.Status = "Active";
                    _context.WorkspaceFederationMembers.Update(fedMember);
                }

                // Repair federations created before chat-room provisioning existed.
                var chatRoom = await _context.ChatRooms
                    .FirstOrDefaultAsync(room => room.FederationId == federationIdVal);
                if (chatRoom == null)
                {
                    await _context.ChatRooms.AddAsync(new ChatRoom
                    {
                        Id = Guid.NewGuid(),
                        FederationId = federationIdVal,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else if (chatRoom.IsDisabled)
                {
                    chatRoom.IsDisabled = false;
                }
            }

            // Update invitation status
            invitation.Status = "Accepted";
            await _context.SaveChangesAsync();

            // Send notification to inviter asynchronously after save is completed
            if (invitation.WorkspaceId.HasValue)
            {
                var msg = $"{user.FullName} has accepted the invitation to join Workspace '{targetName}' as '{invitation.Role}'.";
                await _notificationService.CreateAndSendNotificationAsync(
                    invitation.InviterId,
                    msg,
                    "InvitationAccepted",
                    $"/WorkspaceDetail/{returnJoinCode}",
                    invitation.WorkspaceId.Value
                );
            }
            else if (invitation.FederationId.HasValue)
            {
                var msg = $"{user.FullName} has accepted the invitation to join Federation '{targetName}' as '{invitation.Role ?? "Member"}'.";
                await _notificationService.CreateAndSendNotificationAsync(
                    invitation.InviterId,
                    msg,
                    "InvitationAccepted",
                    "/workspaces",
                    invitation.FederationId.Value
                );
            }

            // Clear general workspaces cache keys
            _cache.Remove($"UserWorkspaces_{user.Id}");
            _cache.Remove($"UserWorkspaces_{invitation.InviterId}");
            _cache.Remove($"UserTasks_{user.Id}");

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
