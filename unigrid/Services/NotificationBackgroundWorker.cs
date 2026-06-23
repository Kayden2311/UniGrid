using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System.Linq;

namespace unigrid.Services
{
    public class NotificationBackgroundWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NotificationBackgroundWorker> _logger;

        public NotificationBackgroundWorker(IServiceProvider serviceProvider, ILogger<NotificationBackgroundWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NotificationBackgroundWorker: Startup successful.");

            // Wait 10 seconds before running the initial check to allow the web app to fully boot up
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("NotificationBackgroundWorker: Beginning deadline and subscription scan...");
                    await CheckTaskDeadlinesAsync();
                    await CheckSubscriptionExpirationsAsync();
                    _logger.LogInformation("NotificationBackgroundWorker: Scan completed successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NotificationBackgroundWorker: Error occurred while scanning.");
                }

                // Run every 12 hours
                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
            }
        }

        private async Task CheckTaskDeadlinesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<UniGridDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;
            var threshold = now.AddHours(48);

            // Find tasks that are not Done (Status != 3), have a DueDate within 48 hours, and have an assignee
            var tasks = await context.Tasks
                .Include(t => t.Workspace)
                .Where(t => t.Status != 3 && t.DueDate.HasValue && t.DueDate.Value > now && t.DueDate.Value <= threshold && t.AssigneeId.HasValue)
                .ToListAsync();

            foreach (var task in tasks)
            {
                // Check if a deadline notification already exists for this task to avoid spamming the user
                var alreadyNotified = await context.Notifications
                    .AnyAsync(n => n.RelatedId == task.Id && n.Type == "TaskDeadlineClose");

                if (!alreadyNotified)
                {
                    _logger.LogInformation("NotificationBackgroundWorker: Task '{Title}' ({Id}) is due soon. Creating notification.", task.Title, task.Id);
                    var message = $"Urgent: Task '{task.Title}' in Workspace '{task.Workspace?.Name ?? "General"}' is due in less than 48 hours (Deadline: {task.DueDate.Value.ToLocalTime():yyyy-MM-dd HH:mm}).";
                    
                    await notificationService.CreateAndSendNotificationAsync(
                        task.AssigneeId!.Value,
                        message,
                        "TaskDeadlineClose",
                        $"/WorkspaceDetail/{task.Workspace?.JoinCode ?? ""}",
                        task.Id
                    );
                }
            }
        }

        private async Task CheckSubscriptionExpirationsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<UniGridDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;
            var threshold = now.AddDays(3);

            // Find active billings expiring in the next 3 days
            var activeBillings = await context.Billings
                .Include(b => b.Workspace)
                .Where(b => b.Status == "Active" && b.EndDate > now && b.EndDate <= threshold)
                .ToListAsync();

            foreach (var billing in activeBillings)
            {
                var workspace = billing.Workspace;
                if (workspace == null) continue;

                // Check if already notified
                var alreadyNotified = await context.Notifications
                    .AnyAsync(n => n.RelatedId == billing.Id && n.Type == "SubscriptionExpiring");

                if (!alreadyNotified)
                {
                    _logger.LogInformation("NotificationBackgroundWorker: Subscription for Workspace '{Name}' ({Id}) is expiring soon. Creating notification.", workspace.Name, workspace.Id);
                    var message = $"Warning: Your subscription for Workspace '{workspace.Name}' is expiring on {billing.EndDate.ToLocalTime():yyyy-MM-dd}. Please renew to prevent service disruption.";
                    
                    await notificationService.CreateAndSendNotificationAsync(
                        workspace.OwnerId,
                        message,
                        "SubscriptionExpiring",
                        "/Pricing",
                        billing.Id
                    );
                }
            }
        }
    }
}
