using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using unigrid.Models;
using unigrid.Data;

namespace unigrid.Data
{
    public static class DbInitializer
    {
        public static async System.Threading.Tasks.Task InitializeAndSeedAsync(UniGridDbContext context, ILogger logger, bool forcePurge = false)
        {
            logger.LogInformation("DbInitializer: Starting database initialization...");

            // Ensure database and tables exist
            var databaseCreator = context.Database.GetService<IDatabaseCreator>() as IRelationalDatabaseCreator;
            if (databaseCreator != null)
            {
                if (!await databaseCreator.ExistsAsync())
                {
                    logger.LogInformation("DbInitializer: Database does not exist. Creating...");
                    await databaseCreator.CreateAsync();
                }
                if (!await databaseCreator.HasTablesAsync())
                {
                    logger.LogInformation("DbInitializer: No tables found. Creating schema...");
                    await databaseCreator.CreateTablesAsync();
                }
            }
            else
            {
                await context.Database.EnsureCreatedAsync();
            }

            // Self-healing: fix upgraded Personal workspaces that should be Group type
            try
            {
                var upgradedWorkspaces = await context.Workspaces
                    .Where(w => w.WorkspaceType == "Personal" && w.PackageTier != "Personal" && w.PackageTier != "Free")
                    .ToListAsync();
                if (upgradedWorkspaces.Any())
                {
                    foreach (var ws in upgradedWorkspaces)
                        ws.WorkspaceType = "Group";
                    await context.SaveChangesAsync();
                    logger.LogInformation("DbInitializer: Migrated {Count} upgraded workspaces to Group type.", upgradedWorkspaces.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DbInitializer: Self-healing workspace type migration failed.");
            }

            // Self-healing: ensure every workspace owner is also a member
            try
            {
                var workspaces = await context.Workspaces.ToListAsync();
                var members = await context.WorkspaceMembers.ToListAsync();
                var missing = new List<WorkspaceMember>();

                foreach (var ws in workspaces)
                {
                    if (!members.Any(m => m.WorkspaceId == ws.Id && m.UserId == ws.OwnerId))
                    {
                        missing.Add(new WorkspaceMember
                        {
                            WorkspaceId = ws.Id,
                            UserId = ws.OwnerId,
                            Role = "Manager",
                            JoinedAt = DateTime.UtcNow
                        });
                    }
                }

                if (missing.Any())
                {
                    await context.WorkspaceMembers.AddRangeAsync(missing);
                    await context.SaveChangesAsync();
                    logger.LogInformation("DbInitializer: Added {Count} missing owner memberships.", missing.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DbInitializer: Self-healing owner membership migration failed.");
            }

            // Ensure AdminSettings (Plans + OperationCosts) exist in SystemSettings
            // This is system config, not seed data — required for the app to function
            try
            {
                var hasPlans = context.SystemSettings.Any(s => s.SettingKey == "Plans");
                var hasCosts = context.SystemSettings.Any(s => s.SettingKey == "OperationCosts");
                if (!hasPlans || !hasCosts)
                {
                    logger.LogInformation("DbInitializer: Initializing default AdminSettings into SystemSettings...");
                    AdminSettings.Load(context); // triggers auto-seed of defaults if missing
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DbInitializer: Failed to initialize AdminSettings.");
            }

            logger.LogInformation("DbInitializer: Initialization complete.");
        }
    }
}
