using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using unigrid.Data;
using unigrid.Hubs;
using unigrid.Models;

namespace unigrid.Services
{
    public class NotificationService : INotificationService
    {
        private readonly UniGridDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IEmailService _emailService;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            UniGridDbContext context,
            IHubContext<ChatHub> hubContext,
            IEmailService emailService,
            ILogger<NotificationService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _emailService = emailService;
            _logger = logger;
        }

        public async System.Threading.Tasks.Task CreateAndSendNotificationAsync(Guid userId, string message, string type, string? link, Guid? relatedId)
        {
            try
            {
                // 1. Save notification to database
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Message = message,
                    Type = type,
                    Link = link,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    RelatedId = relatedId
                };

                await _context.Notifications.AddAsync(notification);
                await _context.SaveChangesAsync();

                // 2. Fetch User and Account details to find AccountId for SignalR Group and Email
                var user = await _context.Users.Include(u => u.Account).FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    _logger.LogWarning("User with ID {UserId} not found. Cannot send real-time notification or email.", userId);
                    return;
                }

                // 3. Push real-time SignalR notification to the Account's group
                if (user.AccountId != Guid.Empty)
                {
                    _logger.LogInformation("Pushing SignalR notification to group Account_{AccountId}", user.AccountId);
                    await _hubContext.Clients.Group($"Account_{user.AccountId}").SendAsync("ReceiveNotification", new
                    {
                        id = notification.Id,
                        message = notification.Message,
                        type = notification.Type,
                        link = notification.Link,
                        isRead = notification.IsRead,
                        createdAt = notification.CreatedAt,
                        relatedId = notification.RelatedId
                    });
                }

                // 4. Send email asynchronously
                if (user.Account != null && !string.IsNullOrEmpty(user.Account.Email))
                {
                    var subject = GetEmailSubject(type);
                    var body = GetEmailBody(type, user.FullName, message, link);
                    // Do not await the email task directly to avoid blocking caller thread, run in background
                    _ = System.Threading.Tasks.Task.Run(() => _emailService.SendEmailAsync(user.Account.Email, subject, body));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create or send notification to user {UserId}", userId);
            }
        }

        private string GetEmailSubject(string type)
        {
            return type switch
            {
                "TaskAssignment" => "UniGrid - New Task Assigned to You",
                "TaskDeadlineClose" => "UniGrid - Urgent: Task Deadline Approaching",
                "SubscriptionNotification" => "UniGrid - Subscription Activated/Updated",
                "SubscriptionExpiring" => "UniGrid - Action Required: Subscription Expiring Soon",
                "WorkspaceInvite" => "UniGrid - New Workspace Invitation",
                "WorkspaceInvitation" => "UniGrid - New Workspace Invitation",
                "InvitationAccepted" => "UniGrid - Workspace Invitation Accepted",
                "TaskApproved" => "UniGrid - Task Completed & Approved",
                "TaskRework" => "UniGrid - Task Needs Rework",
                "TaskComment" => "UniGrid - New Comment on Your Task",
                "TaskReviewRequest" => "UniGrid - Task Submitted for Your Review",
                _ => "UniGrid - New Notification Alert"
            };
        }

        private string GetEmailBody(string type, string fullName, string message, string? link)
        {
            var appUrl = "https://localhost:7158"; // Default base URL (HTTPS)
            var targetLink = string.IsNullOrEmpty(link) ? appUrl : (link.StartsWith("http") ? link : $"{appUrl}{link}");

            if (type == "WorkspaceInvite" || type == "WorkspaceInvitation")
            {
                return $@"
<div style='font-family: sans-serif; line-height: 1.6; color: #334155; max-width: 600px; margin: 0 auto;'>
    <div style='background-color: #4f46e5; padding: 20px; border-radius: 8px 8px 0 0; text-align: center;'>
        <h1 style='color: white; margin: 0; font-size: 22px; font-weight: bold;'>Workspace Invitation</h1>
    </div>
    <div style='padding: 24px; border: 1px solid #e2e8f0; border-top: none; border-radius: 0 0 8px 8px; background-color: #ffffff;'>
        <p style='font-size: 16px; margin-top: 0;'>Hello, <strong>{fullName}</strong>,</p>
        <p style='font-size: 14px;'>{message}</p>
        <p style='font-size: 14px; margin-bottom: 24px;'>You can view and accept this invitation on your workspaces dashboard by clicking the button below.</p>
        <div style='text-align: center; margin: 28px 0;'>
            <a href='{targetLink}' style='background-color: #4f46e5; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: bold; font-size: 14px; display: inline-block;'>View Invitation</a>
        </div>
        <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 24px 0;' />
        <p style='font-size: 11px; color: #64748b; margin-bottom: 0;'>This is an automated notification from UniGrid. Please do not reply directly to this email.</p>
    </div>
</div>";
            }

            return $@"
<div style='font-family: sans-serif; line-height: 1.6; color: #334155; max-width: 600px; margin: 0 auto;'>
    <div style='background-color: #4f46e5; padding: 20px; border-radius: 8px 8px 0 0; text-align: center;'>
        <h1 style='color: white; margin: 0; font-size: 22px; font-weight: bold;'>UniGrid Update</h1>
    </div>
    <div style='padding: 24px; border: 1px solid #e2e8f0; border-top: none; border-radius: 0 0 8px 8px; background-color: #ffffff;'>
        <p style='font-size: 16px; margin-top: 0;'>Hello, <strong>{fullName}</strong>,</p>
        <p style='font-size: 14px;'>{message}</p>
        <p style='font-size: 14px; margin-bottom: 24px;'>Please click the button below to view details and respond on your workspace dashboard.</p>
        <div style='text-align: center; margin: 28px 0;'>
            <a href='{targetLink}' style='background-color: #4f46e5; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: bold; font-size: 14px; display: inline-block;'>Open Dashboard</a>
        </div>
        <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 24px 0;' />
        <p style='font-size: 11px; color: #64748b; margin-bottom: 0;'>This is an automated notification from UniGrid. Please do not reply directly to this email.</p>
    </div>
</div>";
        }
    }
}
