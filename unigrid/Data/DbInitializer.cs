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
using unigrid.Models;

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
                logger.LogInformation("DbInitializer: Ensuring database is created...");
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

            // 1c. Ensure column IsPublic exists in WorkspaceFiles table (Defense-in-Depth schema migration)
            try
            {
                logger.LogInformation("DbInitializer: Ensuring IsPublic column exists in WorkspaceFiles table...");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceFiles]') AND name = 'IsPublic')
                    BEGIN
                        ALTER TABLE [dbo].[WorkspaceFiles] ADD [IsPublic] BIT NOT NULL DEFAULT 1;
                    END
                ");
                logger.LogInformation("DbInitializer: IsPublic column verified/added successfully.");
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
            catch (Exception ex)
            {
                logger.LogError(ex, "DbInitializer: Failed to run custom column migrations.");
                }

                if (missing.Any())
                {
                    await context.WorkspaceMembers.AddRangeAsync(missing);
                    await context.SaveChangesAsync();
                    logger.LogInformation("DbInitializer: Added {Count} missing owner memberships.", missing.Count);
                }

            // 1h. Ensure billing transaction columns exist in Billings table (Transaction metadata migration)
            try
            {
                logger.LogInformation("DbInitializer: Ensuring billing transaction columns exist in Billings table...");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Billings]') AND name = 'Amount')
                    BEGIN
                        ALTER TABLE [dbo].[Billings] ADD [Amount] DECIMAL(18, 2) NULL;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Billings]') AND name = 'UserId')
                    BEGIN
                        ALTER TABLE [dbo].[Billings] ADD [UserId] UNIQUEIDENTIFIER NULL;
                        ALTER TABLE [dbo].[Billings] ADD CONSTRAINT [FK_Billing_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE SET NULL;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Billings]') AND name = 'PaymentMethod')
                    BEGIN
                        ALTER TABLE [dbo].[Billings] ADD [PaymentMethod] NVARCHAR(100) NULL;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Billings]') AND name = 'TransactionRef')
                    BEGIN
                        ALTER TABLE [dbo].[Billings] ADD [TransactionRef] NVARCHAR(100) NULL;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Billings]') AND name = 'CreatedAt')
                    BEGIN
                        ALTER TABLE [dbo].[Billings] ADD [CreatedAt] DATETIME2 NULL;
                    END
                ");
                logger.LogInformation("DbInitializer: Billing transaction columns verified/added successfully.");
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
