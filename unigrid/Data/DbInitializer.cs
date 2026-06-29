using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using unigrid.Data;
using unigrid.Models;

namespace unigrid.Data
{
    public static class DbInitializer
    {
        public static async System.Threading.Tasks.Task InitializeAndSeedAsync(UniGridDbContext context, ILogger logger, bool forcePurge = false)
        {
            logger.LogInformation("DbInitializer: Starting database initialization...");

            // 1. Ensure Database and Tables Exist
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
                    logger.LogInformation("DbInitializer: Database has no tables. Creating tables from schema...");
                    await databaseCreator.CreateTablesAsync();
                }
            }
            else
            {
                logger.LogInformation("DbInitializer: Ensuring database is created...");
                await context.Database.EnsureCreatedAsync();
            }

            // 1c. Self-healing migration for upgraded workspaces (from Personal to Group)
            try
            {
                var upgradedWorkspaces = await context.Workspaces
                    .Where(w => w.WorkspaceType == "Personal" && w.PackageTier != "Personal")
                    .ToListAsync();
                if (upgradedWorkspaces.Any())
                {
                    foreach (var ws in upgradedWorkspaces)
                    {
                        ws.WorkspaceType = "Group";
                    }
                    await context.SaveChangesAsync();
                    logger.LogInformation("DbInitializer: Successfully migrated {Count} upgraded personal workspaces to Group type.", upgradedWorkspaces.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DbInitializer: Failed to run self-healing migration for upgraded workspaces.");
            }

            // Skip legacy raw SQL Server migrations if we are running on PostgreSQL (Supabase)
            if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                // 1b. Ensure columns RefreshToken and RefreshTokenExpiry exist in Accounts table (Defense-in-Depth schema migration)
                try
            {
                logger.LogInformation("DbInitializer: Ensuring RefreshToken columns exist in Accounts table...");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = 'RefreshToken')
                    BEGIN
                        ALTER TABLE [dbo].[Accounts] ADD [RefreshToken] VARCHAR(512) NULL;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = 'RefreshTokenExpiry')
                    BEGIN
                        ALTER TABLE [dbo].[Accounts] ADD [RefreshTokenExpiry] DATETIME2 NULL;
                    END
                ");
                logger.LogInformation("DbInitializer: RefreshToken columns verified/added successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DbInitializer: Failed to add/verify RefreshToken columns in Accounts table.");
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
                logger.LogError(ex, "DbInitializer: Failed to add/verify IsPublic column in WorkspaceFiles table.");
            }

            // 1d. Ensure WorkspaceFederations, WorkspaceFederationMembers, and FederationId exist (Federated Workspace migration)
            try
            {
                logger.LogInformation("DbInitializer: Performing Federated Workspace migrations...");
                
                // A. Check and create WorkspaceFederations
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceFederations]') AND type in (N'U'))
                    BEGIN
                        CREATE TABLE [dbo].[WorkspaceFederations] (
                            [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                            [Name] NVARCHAR(256) NOT NULL,
                            [JoinCode] NVARCHAR(20) NOT NULL UNIQUE,
                            [OwnerId] UNIQUEIDENTIFIER NOT NULL,
                            [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
                            [SettingsJson] NVARCHAR(MAX) NULL,
                            CONSTRAINT [FK_WorkspaceFederations_Users] FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[Users]([Id])
                        );
                    END
                ");

                // A1. Check and add SettingsJson column to WorkspaceFederations if missing
                await context.Database.ExecuteSqlRawAsync(@"
                    IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceFederations]') AND type in (N'U'))
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceFederations]') AND name = 'SettingsJson')
                        BEGIN
                            ALTER TABLE [dbo].[WorkspaceFederations] ADD [SettingsJson] NVARCHAR(MAX) NULL;
                        END
                    END
                ");

                // B. Check and create WorkspaceFederationMembers
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceFederationMembers]') AND type in (N'U'))
                    BEGIN
                        CREATE TABLE [dbo].[WorkspaceFederationMembers] (
                            [FederationId] UNIQUEIDENTIFIER NOT NULL,
                            [UserId] UNIQUEIDENTIFIER NOT NULL,
                            [PersonalWorkspaceId] UNIQUEIDENTIFIER NULL,
                            [JoinedAt] DATETIME2 DEFAULT GETUTCDATE(),
                            [Role] NVARCHAR(50) NOT NULL DEFAULT 'Member',
                            [Status] NVARCHAR(50) NOT NULL DEFAULT 'Active',
                            PRIMARY KEY ([FederationId], [UserId]),
                            CONSTRAINT [FK_FedMembers_Federations] FOREIGN KEY ([FederationId]) REFERENCES [dbo].[WorkspaceFederations]([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_FedMembers_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
                            CONSTRAINT [FK_FedMembers_Workspaces] FOREIGN KEY ([PersonalWorkspaceId]) REFERENCES [dbo].[Workspaces]([Id]) ON DELETE SET NULL
                        );
                    END
                ");

                // C. Check and add FederationId column to WorkspaceFiles
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceFiles]') AND name = 'FederationId')
                    BEGIN
                        ALTER TABLE [dbo].[WorkspaceFiles] ADD [FederationId] UNIQUEIDENTIFIER NULL;
                        ALTER TABLE [dbo].[WorkspaceFiles] ADD CONSTRAINT [FK_Files_WorkspaceFederations] FOREIGN KEY ([FederationId]) REFERENCES [dbo].[WorkspaceFederations]([Id]) ON DELETE SET NULL;
                    END
                ");

                // D. Check and add non-clustered index on WorkspaceFiles(FederationId)
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkspaceFiles_FederationId' AND object_id = OBJECT_ID(N'[dbo].[WorkspaceFiles]'))
                    BEGIN
                        CREATE INDEX [IX_WorkspaceFiles_FederationId] ON [dbo].[WorkspaceFiles]([FederationId]);
                    END
                ");

                logger.LogInformation("DbInitializer: Federated Workspace migrations completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DbInitializer: Failed to run Federated Workspace database migrations.");
            }

            // 1e. Ensure Notifications and WorkspaceInvitations tables exist
            try
            {
                logger.LogInformation("DbInitializer: Performing Notifications and Workspace Invitations migrations...");

                // A. Check and create Notifications
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Notifications]') AND type in (N'U'))
                    BEGIN
                        CREATE TABLE [dbo].[Notifications] (
                            [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                            [UserId] UNIQUEIDENTIFIER NOT NULL,
                            [Message] NVARCHAR(1000) NOT NULL,
                            [Type] NVARCHAR(100) NOT NULL,
                            [Link] NVARCHAR(500) NULL,
                            [IsRead] BIT NOT NULL DEFAULT 0,
                            [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
                            [RelatedId] UNIQUEIDENTIFIER NULL,
                            CONSTRAINT [FK_Notifications_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
                        );
                    END
                ");

                // B. Check and create WorkspaceInvitations
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceInvitations]') AND type in (N'U'))
                    BEGIN
                        CREATE TABLE [dbo].[WorkspaceInvitations] (
                            [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                            [WorkspaceId] UNIQUEIDENTIFIER NOT NULL,
                            [InviterId] UNIQUEIDENTIFIER NOT NULL,
                            [InviteeEmail] NVARCHAR(256) NOT NULL,
                            [Role] NVARCHAR(50) NOT NULL DEFAULT 'Member',
                            [DisplayRole] NVARCHAR(100) NULL,
                            [Status] NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                            [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
                            CONSTRAINT [FK_Invitations_Workspaces] FOREIGN KEY ([WorkspaceId]) REFERENCES [dbo].[Workspaces]([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_Invitations_Inviter] FOREIGN KEY ([InviterId]) REFERENCES [dbo].[Users]([Id])
                        );
                    END
                ");

                logger.LogInformation("DbInitializer: Notifications and Workspace Invitations migrations completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DbInitializer: Failed to run Notifications or Workspace Invitations database migrations.");
            }

            // 1f. Custom column migrations (BusinessAttribute, WorkspaceType, CompanyName, CompanyTaxCode, CompanyAddress, DisplayRole, TaskId on PersonalSchedules)
            try
            {
                logger.LogInformation("DbInitializer: Ensuring columns BusinessAttribute, WorkspaceType, CompanyName, etc. exist...");

                // Users.BusinessAttribute
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = 'BusinessAttribute')
                    BEGIN
                        ALTER TABLE [dbo].[Users] ADD [BusinessAttribute] NVARCHAR(50) NOT NULL DEFAULT 'normal';
                    END
                ");

                // Workspaces columns
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Workspaces]') AND name = 'WorkspaceType')
                    BEGIN
                        ALTER TABLE [dbo].[Workspaces] ADD [WorkspaceType] NVARCHAR(50) NOT NULL DEFAULT 'Personal';
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Workspaces]') AND name = 'CompanyName')
                    BEGIN
                        ALTER TABLE [dbo].[Workspaces] ADD [CompanyName] NVARCHAR(256) NULL;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Workspaces]') AND name = 'CompanyTaxCode')
                    BEGIN
                        ALTER TABLE [dbo].[Workspaces] ADD [CompanyTaxCode] NVARCHAR(100) NULL;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Workspaces]') AND name = 'CompanyAddress')
                    BEGIN
                        ALTER TABLE [dbo].[Workspaces] ADD [CompanyAddress] NVARCHAR(500) NULL;
                    END
                ");

                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Workspaces]') AND name = 'FederationId')
                    BEGIN
                        ALTER TABLE [dbo].[Workspaces] ADD [FederationId] UNIQUEIDENTIFIER NULL;
                        ALTER TABLE [dbo].[Workspaces] ADD CONSTRAINT [FK_Workspaces_WorkspaceFederations] FOREIGN KEY ([FederationId]) REFERENCES [dbo].[WorkspaceFederations]([Id]) ON DELETE SET NULL;
                    END
                ");

                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Workspaces_FederationId' AND object_id = OBJECT_ID(N'[dbo].[Workspaces]'))
                    BEGIN
                        CREATE NONCLUSTERED INDEX [IX_Workspaces_FederationId] ON [dbo].[Workspaces]([FederationId]);
                    END
                ");

                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Workspaces]') AND name = 'SettingsJson')
                    BEGIN
                        ALTER TABLE [dbo].[Workspaces] ADD [SettingsJson] NVARCHAR(MAX) NULL;
                    END
                ");

                // WorkspaceMembers.DisplayRole
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceMembers]') AND name = 'DisplayRole')
                    BEGIN
                        ALTER TABLE [dbo].[WorkspaceMembers] ADD [DisplayRole] NVARCHAR(100) NULL;
                    END
                ");

                // WorkspaceInvitations.DisplayRole
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceInvitations]') AND name = 'DisplayRole')
                    BEGIN
                        ALTER TABLE [dbo].[WorkspaceInvitations] ADD [DisplayRole] NVARCHAR(100) NULL;
                    END
                ");

                // PersonalSchedules.TaskId and FK constraint
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[PersonalSchedules]') AND name = 'TaskId')
                    BEGIN
                        ALTER TABLE [dbo].[PersonalSchedules] ADD [TaskId] UNIQUEIDENTIFIER NULL;
                        ALTER TABLE [dbo].[PersonalSchedules] ADD CONSTRAINT [FK_PersonalSchedules_Tasks] FOREIGN KEY ([TaskId]) REFERENCES [dbo].[Tasks]([Id]) ON DELETE SET NULL;
                    END
                ");

                // WorkspaceMembers.CanDeleteFile, CanCreateTask, CanEditTask
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceMembers]') AND name = 'CanDeleteFile')
                    BEGIN
                        ALTER TABLE [dbo].[WorkspaceMembers] ADD [CanDeleteFile] BIT NOT NULL DEFAULT 0;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceMembers]') AND name = 'CanCreateTask')
                    BEGIN
                        ALTER TABLE [dbo].[WorkspaceMembers] ADD [CanCreateTask] BIT NOT NULL DEFAULT 1;
                    END
                ");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceMembers]') AND name = 'CanEditTask')
                    BEGIN
                        ALTER TABLE [dbo].[WorkspaceMembers] ADD [CanEditTask] BIT NOT NULL DEFAULT 1;
                    END
                ");

                // PersonalSchedules.TimeZone
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[PersonalSchedules]') AND name = 'TimeZone')
                    BEGIN
                        ALTER TABLE [dbo].[PersonalSchedules] ADD [TimeZone] NVARCHAR(100) NOT NULL DEFAULT 'UTC';
                    END
                ");

                logger.LogInformation("DbInitializer: Custom columns verified/added successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DbInitializer: Failed to run custom column migrations.");
            }

            // 1g. Custom Categories, Counter Tasks, and KPI Targets migrations
            try
            {
                logger.LogInformation("DbInitializer: Performing Task Categories and KPI Targets database migrations...");

                // A. Check and create TaskCategories
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TaskCategories]') AND type in (N'U'))
                    BEGIN
                        CREATE TABLE [dbo].[TaskCategories] (
                            [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                            [WorkspaceId] UNIQUEIDENTIFIER NOT NULL,
                            [Name] NVARCHAR(256) NOT NULL,
                            [Description] NVARCHAR(1000) NULL,
                            [ColorHex] NVARCHAR(7) NOT NULL DEFAULT '#3B82F6',
                            [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            CONSTRAINT [FK_TaskCategories_Workspaces] FOREIGN KEY ([WorkspaceId]) REFERENCES [dbo].[Workspaces]([Id]) ON DELETE CASCADE
                        );
                    END
                ");

                // B. Check and create KpiTargets
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[KpiTargets]') AND type in (N'U'))
                    BEGIN
                        CREATE TABLE [dbo].[KpiTargets] (
                            [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                            [WorkspaceId] UNIQUEIDENTIFIER NOT NULL,
                            [UserId] UNIQUEIDENTIFIER NOT NULL,
                            [CategoryId] UNIQUEIDENTIFIER NOT NULL,
                            [PeriodType] NVARCHAR(20) NOT NULL,
                            [StartDate] DATETIME2 NOT NULL,
                            [EndDate] DATETIME2 NOT NULL,
                            [TargetValue] INT NOT NULL DEFAULT 0,
                            [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            CONSTRAINT [FK_KpiTargets_Workspaces] FOREIGN KEY ([WorkspaceId]) REFERENCES [dbo].[Workspaces]([Id]) ON DELETE NO ACTION,
                            CONSTRAINT [FK_KpiTargets_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE NO ACTION,
                            CONSTRAINT [FK_KpiTargets_Categories] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[TaskCategories]([Id]) ON DELETE CASCADE
                        );
                    END
                ");

                // C. Check and add CategoryId, IsCounterTask, TargetCount, CurrentCount columns to Tasks table
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tasks]') AND name = 'CategoryId')
                    BEGIN
                        ALTER TABLE [dbo].[Tasks] ADD [CategoryId] UNIQUEIDENTIFIER NULL;
                        ALTER TABLE [dbo].[Tasks] ADD CONSTRAINT [FK_Tasks_Categories] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[TaskCategories]([Id]) ON DELETE SET NULL;
                    END
                ");

                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tasks]') AND name = 'IsCounterTask')
                    BEGIN
                        ALTER TABLE [dbo].[Tasks] ADD [IsCounterTask] BIT NOT NULL DEFAULT 0;
                    END
                ");

                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tasks]') AND name = 'TargetCount')
                    BEGIN
                        ALTER TABLE [dbo].[Tasks] ADD [TargetCount] INT NOT NULL DEFAULT 1;
                    END
                ");

                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tasks]') AND name = 'CurrentCount')
                    BEGIN
                        ALTER TABLE [dbo].[Tasks] ADD [CurrentCount] INT NOT NULL DEFAULT 0;
                    END
                ");

                // D. Index optimizations
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_TaskCategories_WorkspaceId' AND object_id = OBJECT_ID(N'[dbo].[TaskCategories]'))
                    BEGIN
                        CREATE NONCLUSTERED INDEX [IX_TaskCategories_WorkspaceId] ON [dbo].[TaskCategories]([WorkspaceId]);
                    END
                ");

                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_KpiTargets_Workspace_User_Category' AND object_id = OBJECT_ID(N'[dbo].[KpiTargets]'))
                    BEGIN
                        CREATE NONCLUSTERED INDEX [IX_KpiTargets_Workspace_User_Category] ON [dbo].[KpiTargets]([WorkspaceId], [UserId], [CategoryId]);
                    END
                ");

                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tasks_CategoryId' AND object_id = OBJECT_ID(N'[dbo].[Tasks]'))
                    BEGIN
                        CREATE NONCLUSTERED INDEX [IX_Tasks_CategoryId] ON [dbo].[Tasks]([CategoryId]);
                    END
                ");

                logger.LogInformation("DbInitializer: Task Categories and KPI Targets database migrations completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DbInitializer: Failed to run Task Categories and KPI Targets database migrations.");
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
                logger.LogError(ex, "DbInitializer: Failed to add/verify billing transaction columns in Billings table.");
            }
            }

            // 2. Check if Alice Nguyen and her User profile exist
            bool hasAlice = false;
            try
            {
                var aliceAcc = await context.Accounts.FirstOrDefaultAsync(a => a.Email == "alice@student.edu");
                if (aliceAcc != null)
                {
                    hasAlice = await context.Users.AnyAsync(u => u.AccountId == aliceAcc.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DbInitializer: Failed to query Accounts table. Attempting to create missing tables...");
                if (databaseCreator != null)
                {
                    try
                    {
                        await databaseCreator.CreateTablesAsync();
                        hasAlice = await context.Accounts.AnyAsync(a => a.Email == "alice@student.edu");
                    }
                    catch (Exception createEx)
                    {
                        logger.LogError(createEx, "DbInitializer: Failed to create tables or query Accounts after creation attempt.");
                    }
                }
            }

            if (!hasAlice || forcePurge)
            {
                logger.LogWarning("DbInitializer: 'alice@student.edu' is missing or forcePurge is enabled. Seeding fresh database records...");

                // 3. Purge existing records in correct dependency order to prevent FK lock constraints
                try
                {
                    logger.LogInformation("DbInitializer: Purging existing records to avoid primary key / constraint conflicts...");
                    context.WorkspaceInvitations.RemoveRange(context.WorkspaceInvitations);
                    context.Notifications.RemoveRange(context.Notifications);
                    context.AuditLogs.RemoveRange(context.AuditLogs);
                    context.WorkspaceFiles.RemoveRange(context.WorkspaceFiles);
                    context.TaskComments.RemoveRange(context.TaskComments);
                    context.PersonalSchedules.RemoveRange(context.PersonalSchedules);
                    context.Tasks.RemoveRange(context.Tasks);
                    context.ChatMessages.RemoveRange(context.ChatMessages);
                    context.ChatRooms.RemoveRange(context.ChatRooms);
                    context.WorkspaceFederationMembers.RemoveRange(context.WorkspaceFederationMembers);
                    context.WorkspaceFederations.RemoveRange(context.WorkspaceFederations);
                    context.WorkspaceMembers.RemoveRange(context.WorkspaceMembers);
                    context.Billings.RemoveRange(context.Billings);
                    context.Workspaces.RemoveRange(context.Workspaces);
                    context.Users.RemoveRange(context.Users);
                    context.Admins.RemoveRange(context.Admins);
                    context.Moderators.RemoveRange(context.Moderators);
                    context.Accounts.RemoveRange(context.Accounts);
                    await context.SaveChangesAsync();
                    logger.LogInformation("DbInitializer: Purged all tables successfully.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "DbInitializer: Non-fatal error while purging database tables. Proceeding to seed...");
                }

                // 4. Seed Accounts (15 accounts total, password is 'password123')
                var accAdmin = new Account { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Email = "admin@unigrid.com", PasswordHash = "password123", Role = 1 };
                var accMod = new Account { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Email = "mod@unigrid.com", PasswordHash = "password123", Role = 3 };
                var accAlice = new Account { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Email = "alice@student.edu", PasswordHash = "password123", Role = 2 };
                var accBob = new Account { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Email = "bob@student.edu", PasswordHash = "password123", Role = 2 };
                var accCharlie = new Account { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Email = "charlie@student.edu", PasswordHash = "password123", Role = 2 };
                var accDiana = new Account { Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), Email = "diana@student.edu", PasswordHash = "password123", Role = 2 };
                var accEve = new Account { Id = Guid.Parse("77777777-7777-7777-7777-777777777777"), Email = "eve@student.edu", PasswordHash = "password123", Role = 2 };
                var accFrank = new Account { Id = Guid.Parse("88888888-7777-6666-5555-444444444444"), Email = "frank@student.edu", PasswordHash = "password123", Role = 2 };
                var accGrace = new Account { Id = Guid.Parse("99999999-8888-7777-6666-555555555555"), Email = "grace@student.edu", PasswordHash = "password123", Role = 2 };
                var accHenry = new Account { Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), Email = "henry@student.edu", PasswordHash = "password123", Role = 2 };
                var accJack = new Account { Id = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"), Email = "jack@student.edu", PasswordHash = "password123", Role = 2 };
                var accKelly = new Account { Id = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000"), Email = "kelly@student.edu", PasswordHash = "password123", Role = 2 };
                var accLiam = new Account { Id = Guid.Parse("dddddddd-eeee-ffff-0000-111111111111"), Email = "liam@student.edu", PasswordHash = "password123", Role = 2 };
                var accOlivia = new Account { Id = Guid.Parse("eeeeeeee-ffff-0000-1111-222222222222"), Email = "olivia@student.edu", PasswordHash = "password123", Role = 2 };
                var accNoah = new Account { Id = Guid.Parse("ffffffff-0000-1111-2222-333333333333"), Email = "noah@student.edu", PasswordHash = "password123", Role = 2 };

                await context.Accounts.AddRangeAsync(accAdmin, accMod, accAlice, accBob, accCharlie, accDiana, accEve, accFrank, accGrace, accHenry, accJack, accKelly, accLiam, accOlivia, accNoah);
                await context.SaveChangesAsync();

                // 5. Seed Profiles (Users)
                var profileAdmin = new Admin { AccountId = accAdmin.Id, FullName = "System Administrator", SuperAdmin = true };
                var profileMod = new Moderator { AccountId = accMod.Id, FullName = "Platform Moderator", Region = "East-Asia" };

                var userAlice = new User { Id = Guid.Parse("AAAAAA11-1111-1111-1111-111111111111"), AccountId = accAlice.Id, FullName = "Alice Nguyen", SubscriptionTier = "Business", BusinessAttribute = "business Include" };
                var userBob = new User { Id = Guid.Parse("BBBBBB22-2222-2222-2222-222222222222"), AccountId = accBob.Id, FullName = "Bob Tran", SubscriptionTier = "Pro", BusinessAttribute = "normal" };
                var userCharlie = new User { Id = Guid.Parse("CCCCCC33-3333-3333-3333-333333333333"), AccountId = accCharlie.Id, FullName = "Charlie Le", SubscriptionTier = "ProPlus", BusinessAttribute = "normal" };
                var userDiana = new User { Id = Guid.Parse("DDDDDD44-4444-4444-4444-444444444444"), AccountId = accDiana.Id, FullName = "Diana Pham", SubscriptionTier = "Personal", BusinessAttribute = "normal" };
                var userEve = new User { Id = Guid.Parse("EEEEEE55-5555-5555-5555-555555555555"), AccountId = accEve.Id, FullName = "Eve Vu", SubscriptionTier = "Free", BusinessAttribute = "normal" };
                var userFrank = new User { Id = Guid.Parse("FFFFFF66-6666-6666-6666-666666666666"), AccountId = accFrank.Id, FullName = "Frank Miller", SubscriptionTier = "Business", BusinessAttribute = "business Include" };
                var userGrace = new User { Id = Guid.Parse("AAAAAA77-7777-7777-7777-777777777777"), AccountId = accGrace.Id, FullName = "Grace Hopper", SubscriptionTier = "Pro", BusinessAttribute = "normal" };
                var userHenry = new User { Id = Guid.Parse("BBBBBB88-8888-8888-8888-888888888888"), AccountId = accHenry.Id, FullName = "Henry Cavill", SubscriptionTier = "ProPlus", BusinessAttribute = "normal" };
                var userJack = new User { Id = Guid.Parse("CCCCCC99-9999-9999-9999-999999999999"), AccountId = accJack.Id, FullName = "Jack Dorsey", SubscriptionTier = "Personal", BusinessAttribute = "normal" };
                var userKelly = new User { Id = Guid.Parse("DDDDDD00-0000-0000-0000-000000000000"), AccountId = accKelly.Id, FullName = "Kelly Clarkson", SubscriptionTier = "Free", BusinessAttribute = "normal" };
                var userLiam = new User { Id = Guid.Parse("EEEEEE11-1111-1111-1111-111111111111"), AccountId = accLiam.Id, FullName = "Liam Nguyen", SubscriptionTier = "Business", BusinessAttribute = "business Include" };
                var userOlivia = new User { Id = Guid.Parse("FFFFFF22-2222-2222-2222-222222222222"), AccountId = accOlivia.Id, FullName = "Olivia Tran", SubscriptionTier = "ProPlus", BusinessAttribute = "normal" };
                var userNoah = new User { Id = Guid.Parse("AAAAAA33-3333-3333-3333-333333333333"), AccountId = accNoah.Id, FullName = "Noah Le", SubscriptionTier = "Personal", BusinessAttribute = "normal" };

                await context.Admins.AddAsync(profileAdmin);
                await context.Moderators.AddAsync(profileMod);
                await context.Users.AddRangeAsync(userAlice, userBob, userCharlie, userDiana, userEve, userFrank, userGrace, userHenry, userJack, userKelly, userLiam, userOlivia, userNoah);
                await context.SaveChangesAsync();

                // 6. Seed Workspaces (11 Workspaces)
                var workspaceSE = new Workspace 
                { 
                    Id = Guid.Parse("99999999-9999-9999-9999-999999999999"), 
                    Name = "Enterprise Portal", 
                    OwnerId = userAlice.Id, 
                    JoinCode = "SE-PRO", 
                    PackageTier = "Business", 
                    WorkspaceType = "Business",
                    CompanyName = "UniGrid Corporation",
                    CompanyTaxCode = "0109988776",
                    CompanyAddress = "456 Enterprise Towers, District 1, HCMC"
                };
                var workspaceWeb = new Workspace 
                { 
                    Id = Guid.Parse("88888888-8888-8888-8888-888888888888"), 
                    Name = "E-Commerce Branch", 
                    OwnerId = userAlice.Id, 
                    JoinCode = "WEB-DEV", 
                    PackageTier = "ProPlus",
                    WorkspaceType = "Group"
                };
                var workspaceCalc = new Workspace 
                { 
                    Id = Guid.Parse("77777777-7777-7777-7777-777777777777"), 
                    Name = "Personal Planner", 
                    OwnerId = userBob.Id, 
                    JoinCode = "MATH-101", 
                    PackageTier = "Personal",
                    WorkspaceType = "Personal"
                };
                var workspacePhysics = new Workspace 
                { 
                    Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), 
                    Name = "Physics Lab", 
                    OwnerId = userAlice.Id, 
                    JoinCode = "PHYS-101", 
                    PackageTier = "Free",
                    WorkspaceType = "Personal"
                };
                var workspaceEnglish = new Workspace 
                { 
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), 
                    Name = "English Composition", 
                    OwnerId = userAlice.Id, 
                    JoinCode = "ENGL-101", 
                    PackageTier = "Free",
                    WorkspaceType = "Personal"
                };
                var workspaceResearch = new Workspace 
                { 
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), 
                    Name = "Research Methods", 
                    OwnerId = userAlice.Id, 
                    JoinCode = "RES-101", 
                    PackageTier = "Free",
                    WorkspaceType = "Personal"
                };
                var workspaceDesign = new Workspace 
                { 
                    Id = Guid.Parse("33333333-2222-1111-0000-999999999999"), 
                    Name = "UX Design Studio", 
                    OwnerId = userBob.Id, 
                    JoinCode = "DSN-FLOW", 
                    PackageTier = "ProPlus",
                    WorkspaceType = "Group"
                };
                var workspaceMobile = new Workspace 
                { 
                    Id = Guid.Parse("22222222-1111-0000-9999-888888888888"), 
                    Name = "Mobile Dev Team", 
                    OwnerId = userCharlie.Id, 
                    JoinCode = "MBL-APP", 
                    PackageTier = "Pro",
                    WorkspaceType = "Group"
                };
                var workspaceGlobal = new Workspace 
                { 
                    Id = Guid.Parse("11111111-0000-9999-8888-777777777777"), 
                    Name = "Global Corporate Operations", 
                    OwnerId = userFrank.Id, 
                    JoinCode = "GLB-OPS", 
                    PackageTier = "Business",
                    WorkspaceType = "Business",
                    CompanyName = "Aperture Science",
                    CompanyTaxCode = "0991122334",
                    CompanyAddress = "789 Enrichment Center Rd, Ohio, US"
                };
                var workspaceAI = new Workspace
                {
                    Id = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444"),
                    Name = "AI R&D Lab",
                    OwnerId = userAlice.Id,
                    JoinCode = "AI-LAB",
                    PackageTier = "ProPlus",
                    WorkspaceType = "Group",
                    SettingsJson = "{\"lockedChannels\":{\"infrastructure\":[\"aaaaaa11-1111-1111-1111-111111111111\",\"bbbbbb22-2222-2222-2222-222222222222\",\"ffffff22-2222-2222-2222-222222222222\"]},\"channelOwners\":{\"ai-models\":\"aaaaaa11-1111-1111-1111-111111111111\",\"infrastructure\":\"bbbbbb22-2222-2222-2222-222222222222\",\"dataset-ops\":\"eeeeee11-1111-1111-1111-111111111111\"},\"channelModerators\":{\"ai-models\":[],\"infrastructure\":[],\"dataset-ops\":[]},\"allChannels\":[\"general\",\"ai-models\",\"infrastructure\",\"dataset-ops\"],\"disabledCreateChannelUsers\":[],\"disabledCreateTaskUsers\":[],\"disabledEditTaskUsers\":[],\"disabledDeleteFileUsers\":[],\"disabledDeleteTaskUsers\":[]}"
                };
                var workspaceData = new Workspace
                {
                    Id = Guid.Parse("bbbbbbbb-2222-3333-4444-555555555555"),
                    Name = "Data Analytics Hub",
                    OwnerId = userBob.Id,
                    JoinCode = "DATA-HUB",
                    PackageTier = "Pro",
                    WorkspaceType = "Group"
                };

                await context.Workspaces.AddRangeAsync(workspaceSE, workspaceWeb, workspaceCalc, workspacePhysics, workspaceEnglish, workspaceResearch, workspaceDesign, workspaceMobile, workspaceGlobal, workspaceAI, workspaceData);
                await context.SaveChangesAsync();

                // 7. Seed Billings
                var billingSE = new Billing 
                { 
                    WorkspaceId = workspaceSE.Id, 
                    PackageId = "business_monthly", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddDays(15),
                    Amount = 899000,
                    UserId = userAlice.Id,
                    PaymentMethod = "Credit Card",
                    TransactionRef = "TXN-SE-001",
                    CreatedAt = DateTime.UtcNow.AddDays(-15)
                };
                var billingWeb = new Billing 
                { 
                    WorkspaceId = workspaceWeb.Id, 
                    PackageId = "proplus_monthly", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddDays(20),
                    Amount = 449000,
                    UserId = userAlice.Id,
                    PaymentMethod = "VNPAY QR",
                    TransactionRef = "TXN-WEB-002",
                    CreatedAt = DateTime.UtcNow.AddDays(-10)
                };
                var billingCalc = new Billing 
                { 
                    WorkspaceId = workspaceCalc.Id, 
                    PackageId = "personal_monthly", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddDays(25),
                    Amount = 40000,
                    UserId = userBob.Id,
                    PaymentMethod = "Momo E-Wallet",
                    TransactionRef = "TXN-CALC-003",
                    CreatedAt = DateTime.UtcNow.AddDays(-5)
                };
                var billingPhysics = new Billing 
                { 
                    WorkspaceId = workspacePhysics.Id, 
                    PackageId = "free_tier", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddYears(10),
                    Amount = 0,
                    UserId = null,
                    PaymentMethod = "System",
                    TransactionRef = "TXN-FREE-000",
                    CreatedAt = DateTime.UtcNow.AddMonths(-1)
                };
                var billingEnglish = new Billing 
                { 
                    WorkspaceId = workspaceEnglish.Id, 
                    PackageId = "free_tier", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddYears(10),
                    Amount = 0,
                    UserId = null,
                    PaymentMethod = "System",
                    TransactionRef = "TXN-FREE-000",
                    CreatedAt = DateTime.UtcNow.AddMonths(-1)
                };
                var billingResearch = new Billing 
                { 
                    WorkspaceId = workspaceResearch.Id, 
                    PackageId = "free_tier", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddYears(10),
                    Amount = 0,
                    UserId = null,
                    PaymentMethod = "System",
                    TransactionRef = "TXN-FREE-000",
                    CreatedAt = DateTime.UtcNow.AddMonths(-1)
                };
                var billingDesign = new Billing 
                { 
                    WorkspaceId = workspaceDesign.Id, 
                    PackageId = "proplus_monthly", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddDays(22),
                    Amount = 449000,
                    UserId = userBob.Id,
                    PaymentMethod = "Bank Transfer",
                    TransactionRef = "TXN-DSN-004",
                    CreatedAt = DateTime.UtcNow.AddDays(-8)
                };
                var billingMobile = new Billing 
                { 
                    WorkspaceId = workspaceMobile.Id, 
                    PackageId = "pro_monthly", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddDays(18),
                    Amount = 299000,
                    UserId = userCharlie.Id,
                    PaymentMethod = "Credit Card",
                    TransactionRef = "TXN-MBL-005",
                    CreatedAt = DateTime.UtcNow.AddDays(-12)
                };
                var billingGlobal = new Billing 
                { 
                    WorkspaceId = workspaceGlobal.Id, 
                    PackageId = "business_monthly", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddDays(24),
                    Amount = 899000,
                    UserId = userFrank.Id,
                    PaymentMethod = "Bank Transfer",
                    TransactionRef = "TXN-GLB-006",
                    CreatedAt = DateTime.UtcNow.AddDays(-6)
                };
                var billingAI = new Billing 
                { 
                    WorkspaceId = workspaceAI.Id, 
                    PackageId = "proplus_monthly", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddDays(26),
                    Amount = 449000,
                    UserId = userAlice.Id,
                    PaymentMethod = "VNPAY QR",
                    TransactionRef = "TXN-AI-007",
                    CreatedAt = DateTime.UtcNow.AddDays(-4)
                };
                var billingData = new Billing 
                { 
                    WorkspaceId = workspaceData.Id, 
                    PackageId = "pro_monthly", 
                    Status = "Active", 
                    EndDate = DateTime.UtcNow.AddDays(28),
                    Amount = 299000,
                    UserId = userBob.Id,
                    PaymentMethod = "Momo E-Wallet",
                    TransactionRef = "TXN-DAT-008",
                    CreatedAt = DateTime.UtcNow.AddDays(-2)
                };

                await context.Billings.AddRangeAsync(billingSE, billingWeb, billingCalc, billingPhysics, billingEnglish, billingResearch, billingDesign, billingMobile, billingGlobal, billingAI, billingData);
                await context.SaveChangesAsync();

                // 8. Seed Members
                await context.WorkspaceMembers.AddRangeAsync(
                    // Enterprise Portal
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userAlice.Id, Role = "Manager", DisplayRole = "Head President" }, 
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userBob.Id, Role = "Vice Manager", DisplayRole = "Tech Lead" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userCharlie.Id, Role = "Member", DisplayRole = "BA Lead" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userDiana.Id, Role = "Member", DisplayRole = "HR Director" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userEve.Id, Role = "Viewer", DisplayRole = "Intern" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userFrank.Id, Role = "Member", DisplayRole = "Senior Architect" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userGrace.Id, Role = "Member", DisplayRole = "Quality Assurance" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userLiam.Id, Role = "Member", DisplayRole = "Lead UI Engineer" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userOlivia.Id, Role = "Member", DisplayRole = "DevOps Specialist" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userNoah.Id, Role = "Viewer", DisplayRole = "Data Intern" },
                    
                    // Web
                    new WorkspaceMember { WorkspaceId = workspaceWeb.Id, UserId = userAlice.Id, Role = "Manager", DisplayRole = "Product Owner" }, 
                    new WorkspaceMember { WorkspaceId = workspaceWeb.Id, UserId = userCharlie.Id, Role = "Member", DisplayRole = "Web Developer" },
                    new WorkspaceMember { WorkspaceId = workspaceWeb.Id, UserId = userBob.Id, Role = "Vice Manager", DisplayRole = "Technical Director" },
                    new WorkspaceMember { WorkspaceId = workspaceWeb.Id, UserId = userHenry.Id, Role = "Member", DisplayRole = "React Engineer" },
                    new WorkspaceMember { WorkspaceId = workspaceWeb.Id, UserId = userLiam.Id, Role = "Member", DisplayRole = "UI Designer" },

                    // Planner
                    new WorkspaceMember { WorkspaceId = workspaceCalc.Id, UserId = userBob.Id, Role = "Manager", DisplayRole = "Student" },

                    // Physics, English, Research
                    new WorkspaceMember { WorkspaceId = workspacePhysics.Id, UserId = userAlice.Id, Role = "Manager", DisplayRole = "Researcher" },
                    new WorkspaceMember { WorkspaceId = workspaceEnglish.Id, UserId = userAlice.Id, Role = "Manager", DisplayRole = "Writer" },
                    new WorkspaceMember { WorkspaceId = workspaceResearch.Id, UserId = userAlice.Id, Role = "Manager", DisplayRole = "Academic Adviser" },

                    // Design
                    new WorkspaceMember { WorkspaceId = workspaceDesign.Id, UserId = userBob.Id, Role = "Manager", DisplayRole = "UX Lead" },
                    new WorkspaceMember { WorkspaceId = workspaceDesign.Id, UserId = userCharlie.Id, Role = "Member", DisplayRole = "UI Designer" },
                    new WorkspaceMember { WorkspaceId = workspaceDesign.Id, UserId = userDiana.Id, Role = "Member", DisplayRole = "User Researcher" },

                    // Mobile
                    new WorkspaceMember { WorkspaceId = workspaceMobile.Id, UserId = userCharlie.Id, Role = "Manager", DisplayRole = "VP of Engineering" },
                    new WorkspaceMember { WorkspaceId = workspaceMobile.Id, UserId = userBob.Id, Role = "Member", DisplayRole = "iOS Lead" },
                    new WorkspaceMember { WorkspaceId = workspaceMobile.Id, UserId = userHenry.Id, Role = "Member", DisplayRole = "Android Developer" },
                    new WorkspaceMember { WorkspaceId = workspaceMobile.Id, UserId = userGrace.Id, Role = "Viewer", DisplayRole = "QA Intern" },

                    // Global
                    new WorkspaceMember { WorkspaceId = workspaceGlobal.Id, UserId = userFrank.Id, Role = "Manager", DisplayRole = "VP of Operations" },
                    new WorkspaceMember { WorkspaceId = workspaceGlobal.Id, UserId = userAlice.Id, Role = "Vice Manager", DisplayRole = "Integration Lead" },
                    new WorkspaceMember { WorkspaceId = workspaceGlobal.Id, UserId = userJack.Id, Role = "Member", DisplayRole = "Systems Admin" },
                    new WorkspaceMember { WorkspaceId = workspaceGlobal.Id, UserId = userKelly.Id, Role = "Viewer", DisplayRole = "Observer" },

                    // AI Lab
                    new WorkspaceMember { WorkspaceId = workspaceAI.Id, UserId = userAlice.Id, Role = "Manager", DisplayRole = "AI Principal Researcher" },
                    new WorkspaceMember { WorkspaceId = workspaceAI.Id, UserId = userBob.Id, Role = "Vice Manager", DisplayRole = "ML Infrastructure Engineer" },
                    new WorkspaceMember { WorkspaceId = workspaceAI.Id, UserId = userLiam.Id, Role = "Member", DisplayRole = "Computer Vision Specialist" },
                    new WorkspaceMember { WorkspaceId = workspaceAI.Id, UserId = userOlivia.Id, Role = "Member", DisplayRole = "Data Operations Lead" },

                    // Data Hub
                    new WorkspaceMember { WorkspaceId = workspaceData.Id, UserId = userBob.Id, Role = "Manager", DisplayRole = "Chief Data Architect" },
                    new WorkspaceMember { WorkspaceId = workspaceData.Id, UserId = userNoah.Id, Role = "Member", DisplayRole = "Analytics Engineer" },
                    new WorkspaceMember { WorkspaceId = workspaceData.Id, UserId = userGrace.Id, Role = "Member", DisplayRole = "Statistician" }
                );
                await context.SaveChangesAsync();

                // 9. Seed ChatRooms
                var crSE = new ChatRoom { Id = Guid.Parse("12345678-1234-1234-1234-123456789012"), WorkspaceId = workspaceSE.Id };
                var crWeb = new ChatRoom { Id = Guid.Parse("23456789-2345-2345-2345-234567890123"), WorkspaceId = workspaceWeb.Id };
                var crDesign = new ChatRoom { Id = Guid.Parse("34567890-3456-3456-3456-345678901234"), WorkspaceId = workspaceDesign.Id };
                var crAI = new ChatRoom { Id = Guid.Parse("45678901-4567-4567-4567-456789012345"), WorkspaceId = workspaceAI.Id };
                var crData = new ChatRoom { Id = Guid.Parse("56789012-5678-5678-5678-567890123456"), WorkspaceId = workspaceData.Id };

                await context.ChatRooms.AddRangeAsync(crSE, crWeb, crDesign, crAI, crData);
                await context.SaveChangesAsync();

                // 10. Seed ChatMessages
                await context.ChatMessages.AddRangeAsync(
                    // SE Portal Messages
                    new ChatMessage { RoomId = crSE.Id, SenderId = userAlice.Id, Content = "Hey everyone! Welcome to our Software Engineering study and workspace group 🎉", SentAt = DateTime.UtcNow.AddHours(-20) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userBob.Id, Content = "Thanks Alice! Excited to collaborate and get the core database and routes done.", SentAt = DateTime.UtcNow.AddHours(-19) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userCharlie.Id, Content = "I have completed the wireframe mockups! Check the Files tab to download and review.", SentAt = DateTime.UtcNow.AddHours(-18) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userDiana.Id, Content = "Great. I will structure the OpenAPI endpoints according to the wireframes.", SentAt = DateTime.UtcNow.AddHours(-16) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userFrank.Id, Content = "Let's make sure to stick to the clean architecture folders layout. Saves pain later.", SentAt = DateTime.UtcNow.AddHours(-15) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userGrace.Id, Content = "Unit test suites are mapped out. I will integrate them as soon as Bob commits the CI/CD pipeline.", SentAt = DateTime.UtcNow.AddHours(-14) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userBob.Id, Content = "CI/CD pipeline is ready! GitHub actions will trigger on every PR now.", SentAt = DateTime.UtcNow.AddHours(-12) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userEve.Id, Content = "Can you guys give me access to the staging link? Need to test UI views.", SentAt = DateTime.UtcNow.AddHours(-10) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userAlice.Id, Content = "Yes Eve, here it is: https://unigrid-staging.azurewebsites.net", SentAt = DateTime.UtcNow.AddHours(-8) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userEve.Id, Content = "Got it! Thank you Alice.", SentAt = DateTime.UtcNow.AddHours(-7) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userFrank.Id, Content = "Has anyone optimized the index queries on the AuditLog tables? They are getting a bit slow.", SentAt = DateTime.UtcNow.AddHours(-5) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userBob.Id, Content = "I did! Created composite indexes on WorkspaceId and Timestamp. Speed is 10x now.", SentAt = DateTime.UtcNow.AddHours(-4) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userLiam.Id, Content = "Refactoring is looking awesome! Added the UI style guide to files.", SentAt = DateTime.UtcNow.AddHours(-3) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userOlivia.Id, Content = "Just upgraded the cluster Helm charts. Deploying to secondary staging now.", SentAt = DateTime.UtcNow.AddHours(-2) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userNoah.Id, Content = "Checked database sync metrics. Replica lag is below 5ms!", SentAt = DateTime.UtcNow.AddHours(-1) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userAlice.Id, Content = "Excellent team effort. Let's do a quick sync up session this week!", SentAt = DateTime.UtcNow.AddMinutes(-30) },

                    // Web Messages
                    new ChatMessage { RoomId = crWeb.Id, SenderId = userAlice.Id, Content = "Welcome to the E-Commerce Branch channel! Stripe integration is our top priority.", SentAt = DateTime.UtcNow.AddHours(-10) },
                    new ChatMessage { RoomId = crWeb.Id, SenderId = userCharlie.Id, Content = "I am structuring the product page schemas. Standard JSON-LD is ready.", SentAt = DateTime.UtcNow.AddHours(-8) },
                    new ChatMessage { RoomId = crWeb.Id, SenderId = userBob.Id, Content = "Product catalog queries are cached using Redis. Page speeds are below 200ms.", SentAt = DateTime.UtcNow.AddHours(-6) },
                    new ChatMessage { RoomId = crWeb.Id, SenderId = userHenry.Id, Content = "Refactored Cart view to standard React functional hooks. Check out the latest commit.", SentAt = DateTime.UtcNow.AddHours(-3) },
                    new ChatMessage { RoomId = crWeb.Id, SenderId = userLiam.Id, Content = "Just polished payment buttons with subtle hover interactions.", SentAt = DateTime.UtcNow.AddHours(-2) },
                    new ChatMessage { RoomId = crWeb.Id, SenderId = userAlice.Id, Content = "Superb! I will run local testing on Cart payment steps today.", SentAt = DateTime.UtcNow.AddHours(-1) },

                    // AI Lab Messages
                    new ChatMessage { RoomId = crAI.Id, SenderId = userAlice.Id, Content = "Starting active multi-node training loop for our custom LLM!", SentAt = DateTime.UtcNow.AddHours(-8) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userBob.Id, Content = "InfiniBand is holding up well, no dropped packets reported.", SentAt = DateTime.UtcNow.AddHours(-6) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userLiam.Id, Content = "Instruction dataset is pristine. Trimmed 50k duplicates yesterday.", SentAt = DateTime.UtcNow.AddHours(-4) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userOlivia.Id, Content = "Checked checkpoints folder, autosave is working seamlessly.", SentAt = DateTime.UtcNow.AddHours(-2) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userAlice.Id, Content = "Amazing. Let's monitor convergence metrics through the weekend.", SentAt = DateTime.UtcNow.AddHours(-1) },

                    // AI Lab - ai-models Channel
                    new ChatMessage { RoomId = crAI.Id, SenderId = userAlice.Id, Content = "[channel:ai-models]Let's discuss our model architecture. I'm thinking of starting with a hybrid decoder-only transformer.", SentAt = DateTime.UtcNow.AddHours(-24) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userLiam.Id, Content = "[channel:ai-models]Should we use RoPE for positional embeddings? It seems to perform better at longer context windows.", SentAt = DateTime.UtcNow.AddHours(-23) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userAlice.Id, Content = "[channel:ai-models]Yes, RoPE is a must. Let's target an 8k context length initially.", SentAt = DateTime.UtcNow.AddHours(-22) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userBob.Id, Content = "[channel:ai-models]We'll need to optimize the attention kernel. FlashAttention-2 is integrated into our training stack, so we're good to go.", SentAt = DateTime.UtcNow.AddHours(-21) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userOlivia.Id, Content = "[channel:ai-models]I've updated the model config file in the repository. Let me know if you want to tweak any hyperparameters.", SentAt = DateTime.UtcNow.AddHours(-20) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userAlice.Id, Content = "[channel:ai-models]Great. I'll launch a small 1B param test run tonight to check loss convergence.", SentAt = DateTime.UtcNow.AddHours(-19) },

                    // AI Lab - infrastructure Channel
                    new ChatMessage { RoomId = crAI.Id, SenderId = userBob.Id, Content = "[channel:infrastructure]H100 node cluster scaling is complete. We now have 8 nodes online (64 GPUs total).", SentAt = DateTime.UtcNow.AddHours(-15) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userOlivia.Id, Content = "[channel:infrastructure]I'm seeing some thermal throttling on node-04 during full load. Can we check the cooling allocation?", SentAt = DateTime.UtcNow.AddHours(-14) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userBob.Id, Content = "[channel:infrastructure]On it. I'll talk to the datacenter team. In the meantime, I set a temporary power limit of 350W on node-04 GPUs.", SentAt = DateTime.UtcNow.AddHours(-13) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userAlice.Id, Content = "[channel:infrastructure]Thanks Bob. Keep me posted. We need the full cluster at 100% capacity for the 70B parameter run next week.", SentAt = DateTime.UtcNow.AddHours(-12) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userBob.Id, Content = "[channel:infrastructure]Good news, the datacenter team verified the airflow blockage. Node-04 is running at normal temperatures now. Power limits restored.", SentAt = DateTime.UtcNow.AddHours(-10) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userOlivia.Id, Content = "[channel:infrastructure]Confirmed. Benchmarks show full throughput without throttling. Cluster is green.", SentAt = DateTime.UtcNow.AddHours(-9) },

                    // AI Lab - dataset-ops Channel
                    new ChatMessage { RoomId = crAI.Id, SenderId = userLiam.Id, Content = "[channel:dataset-ops]The WebText-filtered dataset is clean. We pruned around 12% of duplicate/low-quality documents.", SentAt = DateTime.UtcNow.AddHours(-18) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userOlivia.Id, Content = "[channel:dataset-ops]Nice work Liam. Did you filter out toxic content and PII?", SentAt = DateTime.UtcNow.AddHours(-17) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userLiam.Id, Content = "[channel:dataset-ops]Yes, ran our default regex filters for PII and used a lightweight classifier for hate speech/NSFW content.", SentAt = DateTime.UtcNow.AddHours(-16) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userAlice.Id, Content = "[channel:dataset-ops]Perfect. What's the final token count for this subset?", SentAt = DateTime.UtcNow.AddHours(-15) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userLiam.Id, Content = "[channel:dataset-ops]About 450 billion tokens. Combined with the code and math datasets, we're looking at a total of 1.2 trillion tokens.", SentAt = DateTime.UtcNow.AddHours(-14) },
                    new ChatMessage { RoomId = crAI.Id, SenderId = userBob.Id, Content = "[channel:dataset-ops]Awesome. I'll start pre-staging the data onto the local NVMe cache drives on each GPU node to minimize training latency.", SentAt = DateTime.UtcNow.AddHours(-12) }
                );
                await context.SaveChangesAsync();

                // Set the base date starting from today dynamically
                var currentMonday = DateTime.UtcNow.Date;

                // 11. Seed Tasks (50 Tasks Total)
                var t1Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
                var t2Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
                var t3Id = Guid.Parse("00000000-0000-0000-0000-000000000003");
                var t4Id = Guid.Parse("00000000-0000-0000-0000-000000000004");
                var t5Id = Guid.Parse("00000000-0000-0000-0000-000000000005");
                var t6Id = Guid.Parse("00000000-0000-0000-0000-000000000006");
                var t7Id = Guid.Parse("00000000-0000-0000-0000-000000000007");
                var t8Id = Guid.Parse("00000000-0000-0000-0000-000000000008");
                var t9Id = Guid.Parse("00000000-0000-0000-0000-000000000009");
                var t10Id = Guid.Parse("00000000-0000-0000-0000-000000000010");
                var t11Id = Guid.Parse("00000000-0000-0000-0000-000000000011");
                var t12Id = Guid.Parse("00000000-0000-0000-0000-000000000012");
                var t13Id = Guid.Parse("00000000-0000-0000-0000-000000000013");
                var t14Id = Guid.Parse("00000000-0000-0000-0000-000000000014");
                var t15Id = Guid.Parse("00000000-0000-0000-0000-000000000015");
                var t16Id = Guid.Parse("00000000-0000-0000-0000-000000000016");
                var t17Id = Guid.Parse("00000000-0000-0000-0000-000000000017");
                var t18Id = Guid.Parse("00000000-0000-0000-0000-000000000018");
                var t19Id = Guid.Parse("00000000-0000-0000-0000-000000000019");
                var t20Id = Guid.Parse("00000000-0000-0000-0000-000000000020");
                var t21Id = Guid.Parse("00000000-0000-0000-0000-000000000021");
                var t22Id = Guid.Parse("00000000-0000-0000-0000-000000000022");
                var t23Id = Guid.Parse("00000000-0000-0000-0000-000000000023");
                var t24Id = Guid.Parse("00000000-0000-0000-0000-000000000024");
                var t25Id = Guid.Parse("00000000-0000-0000-0000-000000000025");
                var t26Id = Guid.Parse("00000000-0000-0000-0000-000000000026");
                var t27Id = Guid.Parse("00000000-0000-0000-0000-000000000027");
                var t28Id = Guid.Parse("00000000-0000-0000-0000-000000000028");
                var t29Id = Guid.Parse("00000000-0000-0000-0000-000000000029");
                var t30Id = Guid.Parse("00000000-0000-0000-0000-000000000030");
                var t31Id = Guid.Parse("00000000-0000-0000-0000-000000000031");
                var t32Id = Guid.Parse("00000000-0000-0000-0000-000000000032");
                var t33Id = Guid.Parse("00000000-0000-0000-0000-000000000033");
                var t34Id = Guid.Parse("00000000-0000-0000-0000-000000000034");
                var t35Id = Guid.Parse("00000000-0000-0000-0000-000000000035");
                var t36Id = Guid.Parse("00000000-0000-0000-0000-000000000036");
                var t37Id = Guid.Parse("00000000-0000-0000-0000-000000000037");
                var t38Id = Guid.Parse("00000000-0000-0000-0000-000000000038");
                var t39Id = Guid.Parse("00000000-0000-0000-0000-000000000039");
                var t40Id = Guid.Parse("00000000-0000-0000-0000-000000000040");
                var t41Id = Guid.Parse("00000000-0000-0000-0000-000000000041");
                var t42Id = Guid.Parse("00000000-0000-0000-0000-000000000042");
                var t43Id = Guid.Parse("00000000-0000-0000-0000-000000000043");
                var t44Id = Guid.Parse("00000000-0000-0000-0000-000000000044");
                var t45Id = Guid.Parse("00000000-0000-0000-0000-000000000045");
                var t46Id = Guid.Parse("00000000-0000-0000-0000-000000000046");
                var t47Id = Guid.Parse("00000000-0000-0000-0000-000000000047");
                var t48Id = Guid.Parse("00000000-0000-0000-0000-000000000048");
                var t49Id = Guid.Parse("00000000-0000-0000-0000-000000000049");
                var t50Id = Guid.Parse("00000000-0000-0000-0000-000000000050");

                var tasks = new List<unigrid.Models.Task>
                {
                    // Enterprise Portal (@W_SE)
                    new unigrid.Models.Task { Id = t1Id, WorkspaceId = workspaceSE.Id, AssigneeId = userAlice.Id, Title = "AI Report", Description = "Generate summary and evaluation of modern transformer models.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(2).AddHours(23).AddMinutes(59) },
                    new unigrid.Models.Task { Id = t3Id, WorkspaceId = workspaceSE.Id, AssigneeId = userAlice.Id, Title = "Database Project", Description = "Seeded SQL relational schema draft submission.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(6).AddHours(23).AddMinutes(59) },
                    new unigrid.Models.Task { Id = t7Id, WorkspaceId = workspaceSE.Id, AssigneeId = userBob.Id, Title = "Setup CI/CD Pipeline", Description = "Configure GitHub Actions for automated building, linting, and testing.", Status = 2, Priority = 3, DueDate = currentMonday.AddDays(5) },
                    new unigrid.Models.Task { Id = t8Id, WorkspaceId = workspaceSE.Id, AssigneeId = userEve.Id, Title = "Deploy to Staging", Description = "Configure Azure App Service slot deployment for secondary staging testing.", Status = 2, Priority = 2, DueDate = currentMonday.AddDays(6) },
                    new unigrid.Models.Task { Id = t9Id, WorkspaceId = workspaceSE.Id, AssigneeId = userDiana.Id, Title = "Performance Optimization", Description = "Minimize bundle sizes and optimize database indexes on active queries.", Status = 0, Priority = 1, DueDate = currentMonday.AddDays(12) },
                    new unigrid.Models.Task { Id = t10Id, WorkspaceId = workspaceSE.Id, AssigneeId = userCharlie.Id, Title = "Design System Components", Description = "Assemble beautiful, harmoniously tailored dark mode styled elements.", Status = 1, Priority = 2, DueDate = currentMonday.AddDays(4) },
                    new unigrid.Models.Task { Id = t11Id, WorkspaceId = workspaceSE.Id, AssigneeId = userBob.Id, Title = "Database Seeding", Description = "Compose a denser database seeding script matching the frontend mock data.", Status = 3, Priority = 1, DueDate = currentMonday.AddDays(-2) },
                    new unigrid.Models.Task { Id = t12Id, WorkspaceId = workspaceSE.Id, AssigneeId = userBob.Id, Title = "Error Handling Middleware", Description = "Implement a global ExceptionFilter yielding unified JSON error payloads.", Status = 3, Priority = 2, DueDate = currentMonday.AddDays(-1) },
                    new unigrid.Models.Task { Id = t13Id, WorkspaceId = workspaceSE.Id, AssigneeId = userEve.Id, Title = "File Upload Service", Description = "Build out custom local or S3 document uploads supporting files tab.", Status = 1, Priority = 2, DueDate = currentMonday.AddDays(8) },
                    new unigrid.Models.Task { Id = t14Id, WorkspaceId = workspaceSE.Id, AssigneeId = userDiana.Id, Title = "Notification System", Description = "Send real-time alerts using SignalR and WebSockets upon task actions.", Status = 0, Priority = 3, DueDate = currentMonday.AddDays(10) },
                    new unigrid.Models.Task { Id = t15Id, WorkspaceId = workspaceSE.Id, AssigneeId = userCharlie.Id, Title = "Landing Page", Description = "Polish marketing landing page hero gradients and feature carousels.", Status = 3, Priority = 1, DueDate = currentMonday.AddDays(-3) },
                    new unigrid.Models.Task { Id = t16Id, WorkspaceId = workspaceSE.Id, AssigneeId = userFrank.Id, Title = "Architecture Review", Description = "Review overall structural layer boundaries and clean code guidelines.", Status = 0, Priority = 3, DueDate = null },
                    new unigrid.Models.Task { Id = t17Id, WorkspaceId = workspaceSE.Id, AssigneeId = userGrace.Id, Title = "Integrate Unit Tests", Description = "Write comprehensive unit test fixtures covering business controllers.", Status = 1, Priority = 2, DueDate = currentMonday.AddDays(3) },
                    new unigrid.Models.Task { Id = t18Id, WorkspaceId = workspaceSE.Id, AssigneeId = userBob.Id, Title = "GraphQL Gateway Setup", Description = "Design federation gateway layer resolving queries in microservices.", Status = 2, Priority = 3, DueDate = currentMonday.AddDays(7) },
                    new unigrid.Models.Task { Id = t19Id, WorkspaceId = workspaceSE.Id, AssigneeId = userCharlie.Id, Title = "Audit Log Implementation", Description = "Write interceptors saving workspace action audit trails to DB.", Status = 3, Priority = 2, DueDate = currentMonday.AddDays(-5) },
                    new unigrid.Models.Task { Id = t35Id, WorkspaceId = workspaceSE.Id, AssigneeId = userLiam.Id, Title = "Refactor State Management", Description = "Clean up state mutations and implement centralized store hooks.", Status = 0, Priority = 2, DueDate = currentMonday.AddDays(9) },
                    new unigrid.Models.Task { Id = t36Id, WorkspaceId = workspaceSE.Id, AssigneeId = userOlivia.Id, Title = "Kubernetes Deployment Config", Description = "Update Helm charts and ingress configurations for multi-region hosting.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(5) },
                    new unigrid.Models.Task { Id = t37Id, WorkspaceId = workspaceSE.Id, AssigneeId = userNoah.Id, Title = "Database Replication Check", Description = "Review transaction logs, backup validity, and read-replica replication lag.", Status = 3, Priority = 1, DueDate = currentMonday.AddDays(-4) },
                    new unigrid.Models.Task { Id = t38Id, WorkspaceId = workspaceSE.Id, AssigneeId = userAlice.Id, Title = "Corporate Governance Compliance", Description = "Ensure SOC2 Type II structural compliance checklists are filled.", Status = 0, Priority = 3, DueDate = currentMonday.AddDays(15) },
                    new unigrid.Models.Task { Id = t39Id, WorkspaceId = workspaceSE.Id, AssigneeId = userFrank.Id, Title = "System Architecture Guide V3", Description = "Produce extensive software architecture diagrams and design blueprints.", Status = 2, Priority = 3, DueDate = currentMonday.AddDays(4) },
                    new unigrid.Models.Task { Id = t50Id, WorkspaceId = workspaceSE.Id, AssigneeId = userGrace.Id, Title = "End-to-End Visual Playwright Tests", Description = "Draft visual snapshot assertion testing files covering UI components.", Status = 1, Priority = 2, DueDate = currentMonday.AddDays(8) },

                    // E-Commerce Branch (@W_Web)
                    new unigrid.Models.Task { Id = t20Id, WorkspaceId = workspaceWeb.Id, AssigneeId = userAlice.Id, Title = "Stripe Checkout", Description = "Integrate Apple Pay and Stripe Elements in Cart view.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(4) },
                    new unigrid.Models.Task { Id = t21Id, WorkspaceId = workspaceWeb.Id, AssigneeId = userCharlie.Id, Title = "SEO Optimization", Description = "Refactor tags, generate sitemaps, and structure schemas for product pages.", Status = 0, Priority = 1, DueDate = null },
                    new unigrid.Models.Task { Id = t22Id, WorkspaceId = workspaceWeb.Id, AssigneeId = userBob.Id, Title = "Redis Cache Integration", Description = "Cache product listings and category nodes under Redis cluster.", Status = 3, Priority = 2, DueDate = currentMonday.AddDays(-2) },
                    new unigrid.Models.Task { Id = t23Id, WorkspaceId = workspaceWeb.Id, AssigneeId = userHenry.Id, Title = "React Refactoring", Description = "Refactor legacy code to functional components with custom hooks.", Status = 1, Priority = 2, DueDate = currentMonday.AddDays(5) },
                    new unigrid.Models.Task { Id = t40Id, WorkspaceId = workspaceWeb.Id, AssigneeId = userLiam.Id, Title = "Cart Checkout Micro-animations", Description = "Animate cart transitions, item counts, and premium payment checkout flows.", Status = 1, Priority = 1, DueDate = currentMonday.AddDays(3) },
                    new unigrid.Models.Task { Id = t41Id, WorkspaceId = workspaceWeb.Id, AssigneeId = userOlivia.Id, Title = "CDN Edge Cache Tuning", Description = "Optimize static assets delivery routes and enable Brotli compression.", Status = 3, Priority = 2, DueDate = currentMonday.AddDays(-1) },

                    // Personal Planner (@W_Calc)
                    new unigrid.Models.Task { Id = t2Id, WorkspaceId = workspaceCalc.Id, AssigneeId = userAlice.Id, Title = "Math Assignment", Description = "Solve differential equations and triple integrals problem sets.", Status = 0, Priority = 2, DueDate = currentMonday.AddDays(4).AddHours(23).AddMinutes(59) },

                    // Physics Lab (@W_Physics)
                    new unigrid.Models.Task { Id = t4Id, WorkspaceId = workspacePhysics.Id, AssigneeId = userAlice.Id, Title = "Lab Report #3", Description = "Calculate absolute error metrics in electric current fields.", Status = 0, Priority = 2, DueDate = currentMonday.AddDays(3).AddHours(23).AddMinutes(59) },

                    // English Composition (@W_English)
                    new unigrid.Models.Task { Id = t5Id, WorkspaceId = workspaceEnglish.Id, AssigneeId = userAlice.Id, Title = "Essay Draft", Description = "Draft essay arguing for modern architecture paradigms.", Status = 0, Priority = 1, DueDate = currentMonday.AddDays(5).AddHours(23).AddMinutes(59) },

                    // Research Methods (@W_Research)
                    new unigrid.Models.Task { Id = t6Id, WorkspaceId = workspaceResearch.Id, AssigneeId = userAlice.Id, Title = "Literature Review", Description = "Review academic research on adaptive web interfaces.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(4).AddHours(18) },

                    // UX Design Studio (@W_Design)
                    new unigrid.Models.Task { Id = t24Id, WorkspaceId = workspaceDesign.Id, AssigneeId = userBob.Id, Title = "User Research Synthesis", Description = "Map affinity diagrams from user interviews and outline core personas.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(1) },
                    new unigrid.Models.Task { Id = t25Id, WorkspaceId = workspaceDesign.Id, AssigneeId = userCharlie.Id, Title = "Interactive Prototypes", Description = "Construct complex animated prototype transitions inside Figma.", Status = 0, Priority = 2, DueDate = null },
                    new unigrid.Models.Task { Id = t42Id, WorkspaceId = workspaceDesign.Id, AssigneeId = userLiam.Id, Title = "Figma Dark Theme Styling", Description = "Convert core design token overrides to modern sleek slate layouts.", Status = 0, Priority = 2, DueDate = currentMonday.AddDays(6) },
                    new unigrid.Models.Task { Id = t43Id, WorkspaceId = workspaceDesign.Id, AssigneeId = userDiana.Id, Title = "WCAG Accessibility Audit", Description = "Test screen reader landmarks, contrast ratios, and semantic outlines.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(2) },

                    // Mobile Dev Team (@W_Mobile)
                    new unigrid.Models.Task { Id = t44Id, WorkspaceId = workspaceMobile.Id, AssigneeId = userNoah.Id, Title = "Mobile Analytics Event Tracking", Description = "Map custom analytics triggers across user engagement pathways.", Status = 0, Priority = 1, DueDate = currentMonday.AddDays(14) },
                    new unigrid.Models.Task { Id = t45Id, WorkspaceId = workspaceMobile.Id, AssigneeId = userHenry.Id, Title = "APNS & FCM Push Notifications", Description = "Integrate push notification tokens payload parser with notification hub.", Status = 2, Priority = 2, DueDate = currentMonday.AddDays(7) },

                    // Global Corporate Operations (@W_Global)
                    new unigrid.Models.Task { Id = t46Id, WorkspaceId = workspaceGlobal.Id, AssigneeId = userFrank.Id, Title = "Disaster Recovery Simulation", Description = "Run active drill testing failovers to secondary geographic data centers.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(3) },
                    new unigrid.Models.Task { Id = t47Id, WorkspaceId = workspaceGlobal.Id, AssigneeId = userJack.Id, Title = "ISO 27001 Security Audit Prep", Description = "Collate security incident reports, threat models, and logs.", Status = 2, Priority = 2, DueDate = currentMonday.AddDays(6) },
                    new unigrid.Models.Task { Id = t48Id, WorkspaceId = workspaceGlobal.Id, AssigneeId = userKelly.Id, Title = "Corporate Compliance Briefing", Description = "Deliver corporate regulatory updates regarding international tax brackets.", Status = 3, Priority = 1, DueDate = currentMonday.AddDays(-5) },
                    new unigrid.Models.Task { Id = t49Id, WorkspaceId = workspaceGlobal.Id, AssigneeId = userAlice.Id, Title = "Q3 Global Budget Allocation", Description = "Prepare capital expenditure reports and department financial resources.", Status = 0, Priority = 3, DueDate = currentMonday.AddDays(11) },

                    // AI R&D Lab (@W_AI)
                    new unigrid.Models.Task { Id = t26Id, WorkspaceId = workspaceAI.Id, AssigneeId = userAlice.Id, Title = "Train Large Language Model", Description = "Execute multi-node training run of 7B parameter foundation models.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(3) },
                    new unigrid.Models.Task { Id = t27Id, WorkspaceId = workspaceAI.Id, AssigneeId = userBob.Id, Title = "Configure H100 GPU Cluster", Description = "Set up InfiniBand networking and SLURM workload scheduler configs.", Status = 0, Priority = 3, DueDate = currentMonday.AddDays(4) },
                    new unigrid.Models.Task { Id = t28Id, WorkspaceId = workspaceAI.Id, AssigneeId = userLiam.Id, Title = "Dataset Curation & Filtering", Description = "Prune low-quality text tokens and balance instruction tuning records.", Status = 2, Priority = 2, DueDate = currentMonday.AddDays(1) },
                    new unigrid.Models.Task { Id = t29Id, WorkspaceId = workspaceAI.Id, AssigneeId = userOlivia.Id, Title = "MLOps Deployment Ingress", Description = "Package optimized model checkpoints inside Triton Server instances.", Status = 3, Priority = 2, DueDate = currentMonday.AddDays(-2) },
                    new unigrid.Models.Task { Id = t30Id, WorkspaceId = workspaceAI.Id, AssigneeId = null, Title = "Quantize Weights for Edge", Description = "Analyze performance-accuracy trade-offs using 4-bit AWQ compression.", Status = 0, Priority = 1, DueDate = currentMonday.AddDays(10) },

                    // Data Analytics Hub (@W_Data)
                    new unigrid.Models.Task { Id = t31Id, WorkspaceId = workspaceData.Id, AssigneeId = userBob.Id, Title = "ETL Ingestion Pipelines", Description = "Redesign high-throughput Apache Flink stream ingestion jobs.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(2) },
                    new unigrid.Models.Task { Id = t32Id, WorkspaceId = workspaceData.Id, AssigneeId = userNoah.Id, Title = "Corporate KPI Executive Dashboard", Description = "Assemble beautiful visual metric charts inside unified executive report.", Status = 0, Priority = 2, DueDate = currentMonday.AddDays(5) },
                    new unigrid.Models.Task { Id = t33Id, WorkspaceId = workspaceData.Id, AssigneeId = userGrace.Id, Title = "A/B Test Statistical Analysis", Description = "Execute chi-square and t-test formulations over conversion ratios.", Status = 2, Priority = 2, DueDate = currentMonday.AddDays(2) },
                    new unigrid.Models.Task { Id = t34Id, WorkspaceId = workspaceData.Id, AssigneeId = userBob.Id, Title = "Migrate to Snowflake Warehouse", Description = "Port legacy schemas and optimize clustering keys for analytics tables.", Status = 3, Priority = 3, DueDate = currentMonday.AddDays(-3) }
                };

                await context.Tasks.AddRangeAsync(tasks);
                await context.SaveChangesAsync();

                // 12. Seed Task Comments
                await context.TaskComments.AddRangeAsync(
                    new TaskComment { TaskId = t1Id, UserId = userBob.Id, Content = "Which transformer models are we focusing on? GPT-4 and Claude 3.5 Sonnet?", CreatedAt = DateTime.UtcNow.AddHours(-15) },
                    new TaskComment { TaskId = t1Id, UserId = userAlice.Id, Content = "@Bob Let's also include Gemini 1.5 Pro since we are exploring multimodal capabilities.", CreatedAt = DateTime.UtcNow.AddHours(-14) },
                    new TaskComment { TaskId = t1Id, UserId = userFrank.Id, Content = "I think we should also evaluate Llama 3 for local deployment scenarios, to compare cost vs. latency.", CreatedAt = DateTime.UtcNow.AddHours(-12) },
                    new TaskComment { TaskId = t1Id, UserId = userBob.Id, Content = "Good idea. I will compile Llama 3 throughput metrics in the spreadsheet.", CreatedAt = DateTime.UtcNow.AddHours(-10) },
                    new TaskComment { TaskId = t1Id, UserId = userAlice.Id, Content = "Perfect! Please commit the results to the Files repository when done.", CreatedAt = DateTime.UtcNow.AddHours(-9) },

                    new TaskComment { TaskId = t3Id, UserId = userBob.Id, Content = "Seeded DB is set up on local SQL Server. Let me know if anyone runs into FK issues.", CreatedAt = DateTime.UtcNow.AddHours(-8) },
                    new TaskComment { TaskId = t3Id, UserId = userCharlie.Id, Content = "Awesome! Just tested the seeder, works smoothly on my end.", CreatedAt = DateTime.UtcNow.AddHours(-7) },
                    new TaskComment { TaskId = t3Id, UserId = userFrank.Id, Content = "Make sure we set up composite indexes on heavily joined tables. It will drastically save execution times.", CreatedAt = DateTime.UtcNow.AddHours(-5) },

                    new TaskComment { TaskId = t7Id, UserId = userBob.Id, Content = "Workflow action runs successfully on GitHub. Staging deployment is green.", CreatedAt = DateTime.UtcNow.AddHours(-10) },
                    new TaskComment { TaskId = t7Id, UserId = userGrace.Id, Content = "I will perform boundary check testing on the auth endpoints today.", CreatedAt = DateTime.UtcNow.AddHours(-9) },

                    new TaskComment { TaskId = t10Id, UserId = userCharlie.Id, Content = "Just uploaded the Wireframe.png. Please check it out in the Files tab.", CreatedAt = DateTime.UtcNow.AddHours(-4) },
                    new TaskComment { TaskId = t10Id, UserId = userAlice.Id, Content = "The color palette looks very modern Charlie! Fits the premium theme perfectly.", CreatedAt = DateTime.UtcNow.AddHours(-3) },
                    new TaskComment { TaskId = t10Id, UserId = userDiana.Id, Content = "Agreed! Very clean spacing and high-fidelity typography.", CreatedAt = DateTime.UtcNow.AddHours(-2) },
                    new TaskComment { TaskId = t10Id, UserId = userCharlie.Id, Content = "Thanks team! I will start assembling the core component CSS next.", CreatedAt = DateTime.UtcNow.AddHours(-1) },

                    new TaskComment { TaskId = t26Id, UserId = userLiam.Id, Content = "Training loss is looking extremely good Alice! Settled around 1.15 in epoch 3.", CreatedAt = DateTime.UtcNow.AddHours(-4) },
                    new TaskComment { TaskId = t26Id, UserId = userAlice.Id, Content = "Fantastic news Liam. Let's ensure checkpoint files are saved every 500 steps.", CreatedAt = DateTime.UtcNow.AddHours(-3) },
                    new TaskComment { TaskId = t26Id, UserId = userOlivia.Id, Content = "Triton model storage profiles are ready to ingest the checkpoints as soon as training wraps.", CreatedAt = DateTime.UtcNow.AddHours(-2) },

                    new TaskComment { TaskId = t31Id, UserId = userNoah.Id, Content = "Ingestion jobs are lagging about 30 seconds behind the event bus. I am scaling up the consumer slots.", CreatedAt = DateTime.UtcNow.AddHours(-5) },
                    new TaskComment { TaskId = t31Id, UserId = userBob.Id, Content = "Make sure we increase memory allocations symmetrically. Stream states occupy substantial heap.", CreatedAt = DateTime.UtcNow.AddHours(-3) }
                );
                await context.SaveChangesAsync();

                // 13. Seed Personal Schedules
                await context.PersonalSchedules.AddRangeAsync(
                    // Alice
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Study AI", Description = "{\"desc\":\"Review chapters 5-7\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddHours(9), EndTime = currentMonday.AddHours(11) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Team Meeting", Description = "{\"desc\":\"Sprint review\",\"priority\":\"medium\",\"color\":1}", StartTime = currentMonday.AddHours(11), EndTime = currentMonday.AddHours(12) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Gym Workout", Description = "{\"desc\":\"Lower body focus\",\"priority\":\"low\",\"color\":2}", StartTime = currentMonday.AddHours(17), EndTime = currentMonday.AddHours(18) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "AI Report Session", Description = "{\"desc\":\"Write AI evaluation\",\"priority\":\"high\",\"color\":3}", StartTime = currentMonday.AddDays(2).AddHours(13), EndTime = currentMonday.AddDays(2).AddHours(15), TaskId = t1Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Database Project Prep", Description = "{\"desc\":\"SQL Schema drafts\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddDays(1).AddHours(10), EndTime = currentMonday.AddDays(1).AddHours(12), TaskId = t3Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Math Practice Prep", Description = "{\"desc\":\"Diff equations practice\",\"priority\":\"medium\",\"color\":3}", StartTime = currentMonday.AddDays(4).AddHours(8), EndTime = currentMonday.AddDays(4).AddHours(10), TaskId = t2Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Physics Lab Session", Description = "{\"desc\":\"Prepare error analysis charts\",\"priority\":\"medium\",\"color\":1}", StartTime = currentMonday.AddDays(2).AddHours(8), EndTime = currentMonday.AddDays(2).AddHours(9).AddMinutes(30), TaskId = t4Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Essay Writing Block", Description = "{\"desc\":\"Architecture paradigms essay\",\"priority\":\"low\",\"color\":2}", StartTime = currentMonday.AddDays(3).AddHours(14), EndTime = currentMonday.AddDays(3).AddHours(16), TaskId = t5Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Literature Review Reading", Description = "{\"desc\":\"Adaptive UI systems review\",\"priority\":\"high\",\"color\":1}", StartTime = currentMonday.AddDays(5).AddHours(9), EndTime = currentMonday.AddDays(5).AddHours(11), TaskId = t6Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Weekly Alignment sync", Description = "{\"desc\":\"Review active tasks\",\"priority\":\"low\",\"color\":4}", StartTime = currentMonday.AddDays(3).AddHours(9), EndTime = currentMonday.AddDays(3).AddHours(10) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Stripe Payment Integration", Description = "{\"desc\":\"Stripe Apple Pay sandbox\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddDays(4).AddHours(13), EndTime = currentMonday.AddDays(4).AddHours(15), TaskId = t20Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "LLM Training Supervision", Description = "{\"desc\":\"Assess loss curve checkpoints\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddDays(3).AddHours(10), EndTime = currentMonday.AddDays(3).AddHours(12), TaskId = t26Id },

                    // Bob
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userBob.Id, Title = "Setup CI/CD Pipeline Slot", Description = "{\"desc\":\"Action flows\",\"priority\":\"high\",\"color\":3}", StartTime = currentMonday.AddDays(1).AddHours(9), EndTime = currentMonday.AddDays(1).AddHours(11), TaskId = t7Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userBob.Id, Title = "GraphQL Gateway Review", Description = "{\"desc\":\"GraphQL resolvers\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddDays(2).AddHours(14), EndTime = currentMonday.AddDays(2).AddHours(16), TaskId = t18Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userBob.Id, Title = "DB index synthesis", Description = "{\"desc\":\"Optimize AuditLog\",\"priority\":\"medium\",\"color\":1}", StartTime = currentMonday.AddDays(3).AddHours(10), EndTime = currentMonday.AddDays(3).AddHours(12) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userBob.Id, Title = "Gym Session", Description = "{\"desc\":\"Cardio block\",\"priority\":\"low\",\"color\":2}", StartTime = currentMonday.AddDays(4).AddHours(16), EndTime = currentMonday.AddDays(4).AddHours(17) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userBob.Id, Title = "GPU Cluster Verification", Description = "{\"desc\":\"Verify InfiniBand state\",\"priority\":\"high\",\"color\":3}", StartTime = currentMonday.AddDays(4).AddHours(13), EndTime = currentMonday.AddDays(4).AddHours(15), TaskId = t27Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userBob.Id, Title = "ETL Design Workshop", Description = "{\"desc\":\"Flink task slots design\",\"priority\":\"medium\",\"color\":0}", StartTime = currentMonday.AddDays(2).AddHours(10), EndTime = currentMonday.AddDays(2).AddHours(12), TaskId = t31Id },

                    // Diana
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userDiana.Id, Title = "HR Onboarding Prep", Description = "{\"desc\":\"Study portal profiles\",\"priority\":\"medium\",\"color\":2}", StartTime = currentMonday.AddDays(1).AddHours(8), EndTime = currentMonday.AddDays(1).AddHours(10) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userDiana.Id, Title = "Figma UX Interview analysis", Description = "{\"desc\":\"Synthing affinity diagrams\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddDays(3).AddHours(13), EndTime = currentMonday.AddDays(3).AddHours(15), TaskId = t24Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userDiana.Id, Title = "WCAG Review Blocks", Description = "{\"desc\":\"Contrast tests on portal\",\"priority\":\"medium\",\"color\":1}", StartTime = currentMonday.AddDays(2).AddHours(14), EndTime = currentMonday.AddDays(2).AddHours(16), TaskId = t43Id },

                    // Liam
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userLiam.Id, Title = "State Management Redesign", Description = "{\"desc\":\"Refactor Redux stores\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddDays(2).AddHours(9), EndTime = currentMonday.AddDays(2).AddHours(12), TaskId = t35Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userLiam.Id, Title = "Micro-animations Drafting", Description = "{\"desc\":\"Framer motion transitions\",\"priority\":\"low\",\"color\":1}", StartTime = currentMonday.AddDays(3).AddHours(14), EndTime = currentMonday.AddDays(3).AddHours(16), TaskId = t40Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userLiam.Id, Title = "UI tokens alignment", Description = "{\"desc\":\"Dark layouts and buttons\",\"priority\":\"medium\",\"color\":2}", StartTime = currentMonday.AddDays(5).AddHours(10), EndTime = currentMonday.AddDays(5).AddHours(12), TaskId = t42Id },

                    // Olivia
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userOlivia.Id, Title = "K8s Cluster Upgrade", Description = "{\"desc\":\"Apply ingress and secrets\",\"priority\":\"high\",\"color\":3}", StartTime = currentMonday.AddDays(5).AddHours(8), EndTime = currentMonday.AddDays(5).AddHours(11), TaskId = t36Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userOlivia.Id, Title = "Triton Setup Block", Description = "{\"desc\":\"Model repository layout\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddDays(1).AddHours(13), EndTime = currentMonday.AddDays(1).AddHours(15), TaskId = t29Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userOlivia.Id, Title = "Release Deployment check", Description = "{\"desc\":\"Deploy staging artifact\",\"priority\":\"low\",\"color\":4}", StartTime = currentMonday.AddDays(2).AddHours(16), EndTime = currentMonday.AddDays(2).AddHours(17) },

                    // Noah
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userNoah.Id, Title = "Audit Database Replica", Description = "{\"desc\":\"Verify replication streams\",\"priority\":\"high\",\"color\":1}", StartTime = currentMonday.AddDays(1).AddHours(10), EndTime = currentMonday.AddDays(1).AddHours(12), TaskId = t37Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userNoah.Id, Title = "Analytics Tracking Schema", Description = "{\"desc\":\"Engagement mapping\",\"priority\":\"low\",\"color\":2}", StartTime = currentMonday.AddDays(3).AddHours(14), EndTime = currentMonday.AddDays(3).AddHours(16), TaskId = t44Id },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userNoah.Id, Title = "Executive Dashboard Assembly", Description = "{\"desc\":\"Draw charts inside view\",\"priority\":\"medium\",\"color\":0}", StartTime = currentMonday.AddDays(4).AddHours(9), EndTime = currentMonday.AddDays(4).AddHours(11), TaskId = t32Id }
                );
                await context.SaveChangesAsync();

                // 14. Seed Workspace Files
                var file1 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, TaskId = t1Id, UserId = userAlice.Id, FileName = "Transformer_Comparison.pdf", FileUrl = $"files/{workspaceSE.Id}/transformer_comparison.pdf", FileType = "pdf", FileSize = 2516582, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-4) };
                var file2 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, TaskId = t3Id, UserId = userBob.Id, FileName = "Database_Schema_Draft.docx", FileUrl = $"files/{workspaceSE.Id}/db_schema.docx", FileType = "doc", FileSize = 1153433, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-3) };
                var file3 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, UserId = userDiana.Id, FileName = "Budget.xlsx", FileUrl = $"files/{workspaceSE.Id}/budget.xlsx", FileType = "spreadsheet", FileSize = 348160, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-2) };
                var file4 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, UserId = userCharlie.Id, FileName = "Wireframe.png", FileUrl = $"files/{workspaceSE.Id}/wireframe.png", FileType = "image", FileSize = 4404019, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-1) };
                var file5 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, TaskId = t12Id, UserId = userFrank.Id, FileName = "Architecture_Spec_V2.pdf", FileUrl = $"files/{workspaceSE.Id}/architecture_spec.pdf", FileType = "pdf", FileSize = 3145728, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-5) };
                var file6 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, TaskId = t7Id, UserId = userBob.Id, FileName = "CICD_Flowchart.png", FileUrl = $"files/{workspaceSE.Id}/cicd_flow.png", FileType = "image", FileSize = 1048576, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-6) };
                var file7 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, UserId = userGrace.Id, FileName = "QA_Test_Scenarios.xlsx", FileUrl = $"files/{workspaceSE.Id}/qa_scenarios.xlsx", FileType = "spreadsheet", FileSize = 524288, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-7) };
                var file8 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, TaskId = t35Id, UserId = userLiam.Id, FileName = "Enterprise_UI_Style_Guide.pdf", FileUrl = $"files/{workspaceSE.Id}/ui_style_guide.pdf", FileType = "pdf", FileSize = 8912896, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-8) };

                // Web
                var file9 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceWeb.Id, TaskId = t20Id, UserId = userAlice.Id, FileName = "Stripe_API_Integration.pdf", FileUrl = $"files/{workspaceWeb.Id}/stripe_api.pdf", FileType = "pdf", FileSize = 1572864, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-10) };
                var file10 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceWeb.Id, UserId = userCharlie.Id, FileName = "SEO_Audit_Report.docx", FileUrl = $"files/{workspaceWeb.Id}/seo_audit.docx", FileType = "doc", FileSize = 2097152, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-8) };
                var file11 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceWeb.Id, TaskId = t22Id, UserId = userBob.Id, FileName = "Redis_Benchmarking_Results.xlsx", FileUrl = $"files/{workspaceWeb.Id}/redis_bench.xlsx", FileType = "spreadsheet", FileSize = 819200, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-6) };

                // Design
                var file12 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceDesign.Id, TaskId = t24Id, UserId = userBob.Id, FileName = "User_Personas_Mockup.pdf", FileUrl = $"files/{workspaceDesign.Id}/personas.pdf", FileType = "pdf", FileSize = 4194304, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-12) };
                var file13 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceDesign.Id, UserId = userDiana.Id, FileName = "Figma_Export_Assets.zip", FileUrl = $"files/{workspaceDesign.Id}/figma_assets.zip", FileType = "zip", FileSize = 15728640, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-13) };

                // AI Lab
                var file14 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceAI.Id, TaskId = t26Id, UserId = userAlice.Id, FileName = "Transformer_Weights_V1.bin", FileUrl = $"files/{workspaceAI.Id}/transformer_weights.bin", FileType = "binary", FileSize = 1288490188, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-4) };
                var file15 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceAI.Id, TaskId = t27Id, UserId = userBob.Id, FileName = "GPU_Cluster_Config.yaml", FileUrl = $"files/{workspaceAI.Id}/gpu_cluster_config.yaml", FileType = "config", FileSize = 46080, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-5) };

                // Data Hub
                var file16 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceData.Id, TaskId = t32Id, UserId = userNoah.Id, FileName = "ETL_Pipeline_Flow.drawio", FileUrl = $"files/{workspaceData.Id}/etl_pipeline_flow.drawio", FileType = "image", FileSize = 122880, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-6) };

                await context.WorkspaceFiles.AddRangeAsync(file1, file2, file3, file4, file5, file6, file7, file8, file9, file10, file11, file12, file13, file14, file15, file16);
                await context.SaveChangesAsync();

                // 15. Seed Workspace Federations (Federation Model)
                var fedId = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
                var fedAcademicId = Guid.Parse("EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE");
                var fedCloudId = Guid.Parse("DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD");

                var federation = new WorkspaceFederation
                {
                    Id = fedId,
                    Name = "Store Integration Federation",
                    JoinCode = "FED-STORE",
                    OwnerId = userAlice.Id,
                    CreatedAt = DateTime.UtcNow
                };
                var federationAcademic = new WorkspaceFederation
                {
                    Id = fedAcademicId,
                    Name = "Academic Collaboration Alliance",
                    JoinCode = "FED-ACAD",
                    OwnerId = userBob.Id,
                    CreatedAt = DateTime.UtcNow
                };
                var federationCloud = new WorkspaceFederation
                {
                    Id = fedCloudId,
                    Name = "Cloud Architecture Alliance",
                    JoinCode = "FED-CLOUD",
                    OwnerId = userBob.Id,
                    CreatedAt = DateTime.UtcNow
                };

                await context.WorkspaceFederations.AddRangeAsync(federation, federationAcademic, federationCloud);
                await context.SaveChangesAsync();

                // 16. Seed Workspace Federation Members
                var fedMember1 = new WorkspaceFederationMember { FederationId = fedId, UserId = userAlice.Id, PersonalWorkspaceId = workspaceWeb.Id, JoinedAt = DateTime.UtcNow, Role = "HeadPresident", Status = "Active" };
                var fedMember2 = new WorkspaceFederationMember { FederationId = fedId, UserId = userBob.Id, PersonalWorkspaceId = workspaceCalc.Id, JoinedAt = DateTime.UtcNow, Role = "Member", Status = "PendingOwnerApproval" };
                var fedMemberManager = new WorkspaceFederationMember { FederationId = fedId, UserId = userFrank.Id, PersonalWorkspaceId = null, JoinedAt = DateTime.UtcNow, Role = "DepartmentManager", Status = "Active" };
                var fedMemberManager2 = new WorkspaceFederationMember { FederationId = fedId, UserId = userGrace.Id, PersonalWorkspaceId = null, JoinedAt = DateTime.UtcNow, Role = "DepartmentManager", Status = "Active" };
                
                var fedMember3 = new WorkspaceFederationMember { FederationId = fedAcademicId, UserId = userBob.Id, PersonalWorkspaceId = workspaceDesign.Id, JoinedAt = DateTime.UtcNow, Role = "HeadPresident", Status = "Active" };
                var fedMember4 = new WorkspaceFederationMember { FederationId = fedAcademicId, UserId = userCharlie.Id, PersonalWorkspaceId = workspaceMobile.Id, JoinedAt = DateTime.UtcNow, Role = "Member", Status = "Active" };

                var fedMember5 = new WorkspaceFederationMember { FederationId = fedCloudId, UserId = userBob.Id, PersonalWorkspaceId = workspaceCalc.Id, JoinedAt = DateTime.UtcNow, Role = "HeadPresident", Status = "Active" };
                var fedMember6 = new WorkspaceFederationMember { FederationId = fedCloudId, UserId = userCharlie.Id, PersonalWorkspaceId = workspaceMobile.Id, JoinedAt = DateTime.UtcNow, Role = "Member", Status = "Active" };
                var fedMember7 = new WorkspaceFederationMember { FederationId = fedCloudId, UserId = userOlivia.Id, PersonalWorkspaceId = workspaceDesign.Id, JoinedAt = DateTime.UtcNow, Role = "Member", Status = "Active" };

                await context.WorkspaceFederationMembers.AddRangeAsync(fedMember1, fedMember2, fedMemberManager, fedMemberManager2, fedMember3, fedMember4, fedMember5, fedMember6, fedMember7);
                
                // Symmetrically map seeded workspaces to their respective federations
                workspaceWeb.FederationId = fedId;
                workspaceCalc.FederationId = fedId;
                workspaceDesign.FederationId = fedAcademicId;
                workspaceMobile.FederationId = fedAcademicId;
                workspaceAI.FederationId = fedCloudId;
                context.Workspaces.UpdateRange(workspaceWeb, workspaceCalc, workspaceDesign, workspaceMobile, workspaceAI);

                await context.SaveChangesAsync();

                // 17. Seed projected and direct files
                var fedFile1 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceWeb.Id, UserId = userAlice.Id, FileName = "Storefront_Mockups_V1.pdf", FileUrl = $"files/{workspaceWeb.Id}/storefront_mockups_v1.pdf", FileType = "pdf", FileSize = 2202010, IsPublic = true, FederationId = fedId, CreatedAt = DateTime.UtcNow.AddHours(-2) };
                var fedFile2 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceCalc.Id, UserId = userBob.Id, FileName = "Payment_Gateway_Specs.docx", FileUrl = $"files/{workspaceCalc.Id}/payment_gateway_specs.docx", FileType = "doc", FileSize = 1258291, IsPublic = true, FederationId = fedId, CreatedAt = DateTime.UtcNow.AddHours(-1) };
                var fedFileDirect = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = null, UserId = userAlice.Id, FileName = "Federation_Strategy_Q3.pdf", FileUrl = "files/federations/strategy_q3.pdf", FileType = "pdf", FileSize = 5489222, IsPublic = true, FederationId = fedId, CreatedAt = DateTime.UtcNow.AddHours(-4) };
                
                var fedFile3 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceDesign.Id, UserId = userBob.Id, FileName = "Personas_Virt_Export.pdf", FileUrl = $"files/{workspaceDesign.Id}/personas_virt.pdf", FileType = "pdf", FileSize = 3145728, IsPublic = true, FederationId = fedAcademicId, CreatedAt = DateTime.UtcNow.AddHours(-5) };
                var fedFile4 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceMobile.Id, UserId = userCharlie.Id, FileName = "iOS_Architecture_Draft.docx", FileUrl = $"files/{workspaceMobile.Id}/ios_arch.docx", FileType = "doc", FileSize = 1572864, IsPublic = true, FederationId = fedAcademicId, CreatedAt = DateTime.UtcNow.AddHours(-3) };

                var fedFile5 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceAI.Id, UserId = userBob.Id, FileName = "GPU_Architecture_Plan.pdf", FileUrl = $"files/{workspaceAI.Id}/gpu_arch_plan.pdf", FileType = "pdf", FileSize = 4194304, IsPublic = true, FederationId = fedCloudId, CreatedAt = DateTime.UtcNow.AddHours(-4) };
                var fedFile6 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceDesign.Id, UserId = userOlivia.Id, FileName = "UI_Dark_Layout_Grid.png", FileUrl = $"files/{workspaceDesign.Id}/ui_dark_grid.png", FileType = "image", FileSize = 1048576, IsPublic = true, FederationId = fedCloudId, CreatedAt = DateTime.UtcNow.AddHours(-3) };

                await context.WorkspaceFiles.AddRangeAsync(fedFile1, fedFile2, fedFileDirect, fedFile3, fedFile4, fedFile5, fedFile6);
                await context.SaveChangesAsync();

                // Associate projected federation files
                file1.FederationId = fedId;
                file2.FederationId = fedId;
                await context.SaveChangesAsync();

                // 17b. Seed Federation tasks
                var fedTask1 = new unigrid.Models.Task { Id = Guid.NewGuid(), WorkspaceId = null, FederationId = fedId, AssigneeId = userFrank.Id, Title = "Review Q3 Cross-Department Progress", Description = "Analyze metrics and reports from WEB-DEV and MATH-101.", Status = 1, Priority = 3, DueDate = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow };
                var fedTask2 = new unigrid.Models.Task { Id = Guid.NewGuid(), WorkspaceId = null, FederationId = fedId, AssigneeId = userAlice.Id, Title = "Authorize Personal Plan Workspace Connections", Description = "Verify secure invite links and approve Bob Tran's math planner connection.", Status = 0, Priority = 2, DueDate = DateTime.UtcNow.AddDays(3), CreatedAt = DateTime.UtcNow };
                await context.Tasks.AddRangeAsync(fedTask1, fedTask2);
                await context.SaveChangesAsync();

                // 17c. Seed Federation ChatRoom and Messages
                var fedChatRoom = new ChatRoom { Id = Guid.NewGuid(), WorkspaceId = null, FederationId = fedId, CreatedAt = DateTime.UtcNow };
                await context.ChatRooms.AddAsync(fedChatRoom);
                await context.SaveChangesAsync();

                await context.ChatMessages.AddRangeAsync(
                    new ChatMessage { RoomId = fedChatRoom.Id, SenderId = userAlice.Id, Content = "Welcome to the Store Integration Federation Hub! Managers can discuss high-level tasks here.", SentAt = DateTime.UtcNow.AddHours(-5) },
                    new ChatMessage { RoomId = fedChatRoom.Id, SenderId = userFrank.Id, Content = "Reporting in. I have started reviewing the progress reports for the e-commerce branch.", SentAt = DateTime.UtcNow.AddHours(-4) },
                    new ChatMessage { RoomId = fedChatRoom.Id, SenderId = userBob.Id, Content = "Hi Alice, I submitted an integration request for my personal planner. Please authorize it.", SentAt = DateTime.UtcNow.AddHours(-3) },
                    new ChatMessage { RoomId = fedChatRoom.Id, SenderId = userAlice.Id, Content = "@Bob I see your request. Personal workspaces require validation. I will approve it shortly.", SentAt = DateTime.UtcNow.AddHours(-2) }
                );
                await context.SaveChangesAsync();

                // 18. Seed invitations and notifications
                await context.WorkspaceInvitations.AddRangeAsync(
                    new WorkspaceInvitation { Id = Guid.NewGuid(), WorkspaceId = workspaceAI.Id, FederationId = null, InviterId = userAlice.Id, InviteeEmail = "grace@student.edu", Role = "Member", Status = "Pending", CreatedAt = DateTime.UtcNow },
                    new WorkspaceInvitation { Id = Guid.NewGuid(), WorkspaceId = workspaceData.Id, FederationId = null, InviterId = userBob.Id, InviteeEmail = "liam@student.edu", Role = "Member", Status = "Pending", CreatedAt = DateTime.UtcNow },
                    new WorkspaceInvitation { Id = Guid.NewGuid(), WorkspaceId = workspaceWeb.Id, FederationId = null, InviterId = userAlice.Id, InviteeEmail = "olivia@student.edu", Role = "Member", Status = "Accepted", CreatedAt = DateTime.UtcNow },
                    new WorkspaceInvitation { Id = Guid.NewGuid(), WorkspaceId = null, FederationId = fedId, InviterId = userAlice.Id, InviteeEmail = "frank@student.edu", Role = "DepartmentManager", Status = "Accepted", CreatedAt = DateTime.UtcNow }
                );

                await context.Notifications.AddRangeAsync(
                    new Notification { Id = Guid.NewGuid(), UserId = userAlice.Id, Message = "You have been appointed Manager of the new AI R&D Lab.", Type = "WorkspaceInvite", Link = "/workspaces", IsRead = false, CreatedAt = DateTime.UtcNow },
                    new Notification { Id = Guid.NewGuid(), UserId = userBob.Id, Message = "Alice Nguyen assigned you to task: Quantize Weights for Edge.", Type = "TaskAssignment", Link = "/tasks", IsRead = false, CreatedAt = DateTime.UtcNow },
                    new Notification { Id = Guid.NewGuid(), UserId = userLiam.Id, Message = "You have a pending invitation to join: Data Analytics Hub.", Type = "WorkspaceInvite", Link = "/workspaces", IsRead = false, CreatedAt = DateTime.UtcNow },
                    new Notification { Id = Guid.NewGuid(), UserId = userNoah.Id, Message = "Bob Tran assigned you to task: Corporate KPI Executive Dashboard.", Type = "TaskAssignment", Link = "/tasks", IsRead = false, CreatedAt = DateTime.UtcNow }
                );
                await context.SaveChangesAsync();

                logger.LogInformation("DbInitializer: Database seeded successfully.");
            }
            else
            {
                logger.LogInformation("DbInitializer: Database is already seeded with Alice. Checking Federated Workspace records...");
                
                // Double-check password for Alice to ensure it's "password123" (prevents local state mismatch issues)
                var aliceAcc = await context.Accounts.FirstOrDefaultAsync(a => a.Email == "alice@student.edu");
                if (aliceAcc != null && aliceAcc.PasswordHash != "password123")
                {
                    logger.LogInformation("DbInitializer: Resetting Alice's password to password123 to match expected defaults.");
                    aliceAcc.PasswordHash = "password123";
                    await context.SaveChangesAsync();
                }

                // Self-healing seeder: If database exists but federations are empty, seed them dynamically!
                bool hasFeds = await context.WorkspaceFederations.AnyAsync();
                if (!hasFeds)
                {
                    logger.LogInformation("DbInitializer: Seeding missing Federated Workspace records incrementally...");
                    var userAlice = await context.Users.Include(u => u.Account).FirstOrDefaultAsync(u => u.Account.Email == "alice@student.edu");
                    var userBob = await context.Users.Include(u => u.Account).FirstOrDefaultAsync(u => u.Account.Email == "bob@student.edu");
                    var workspaceWeb = await context.Workspaces.FirstOrDefaultAsync(w => w.JoinCode == "WEB-DEV");
                    var workspaceCalc = await context.Workspaces.FirstOrDefaultAsync(w => w.JoinCode == "MATH-101");

                    if (userAlice != null && userBob != null && workspaceWeb != null && workspaceCalc != null)
                    {
                        var fedId = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
                        var federation = new WorkspaceFederation
                        {
                            Id = fedId,
                            Name = "Store Integration Federation",
                            JoinCode = "FED-STORE",
                            OwnerId = userAlice.Id,
                            CreatedAt = DateTime.UtcNow
                        };
                        await context.WorkspaceFederations.AddAsync(federation);

                        var fedMember1 = new WorkspaceFederationMember
                        {
                            FederationId = fedId,
                            UserId = userAlice.Id,
                            PersonalWorkspaceId = workspaceWeb.Id,
                            JoinedAt = DateTime.UtcNow
                        };
                        var fedMember2 = new WorkspaceFederationMember
                        {
                            FederationId = fedId,
                            UserId = userBob.Id,
                            PersonalWorkspaceId = workspaceCalc.Id,
                            JoinedAt = DateTime.UtcNow
                        };
                        await context.WorkspaceFederationMembers.AddRangeAsync(fedMember1, fedMember2);

                        // Symmetrically map seeded workspaces to their federation in self-healing
                        workspaceWeb.FederationId = fedId;
                        workspaceCalc.FederationId = fedId;
                        context.Workspaces.UpdateRange(workspaceWeb, workspaceCalc);

                        await context.SaveChangesAsync();

                        // Seed projected files if they don't exist yet
                        var hasMockupFile = await context.WorkspaceFiles.AnyAsync(f => f.FileName == "Storefront_Mockups_V1.pdf");
                        if (!hasMockupFile)
                        {
                            var fedFile1 = new WorkspaceFile 
                            { 
                                Id = Guid.NewGuid(), 
                                WorkspaceId = workspaceWeb.Id, 
                                UserId = userAlice.Id, 
                                FileName = "Storefront_Mockups_V1.pdf", 
                                FileUrl = $"files/{workspaceWeb.Id}/storefront_mockups_v1.pdf", 
                                FileType = "pdf", 
                                FileSize = 2202010, 
                                IsPublic = true, 
                                FederationId = fedId, 
                                CreatedAt = DateTime.UtcNow.AddHours(-2) 
                            };
                            var fedFile2 = new WorkspaceFile 
                            { 
                                Id = Guid.NewGuid(), 
                                WorkspaceId = workspaceCalc.Id, 
                                UserId = userBob.Id, 
                                FileName = "Payment_Gateway_Specs.docx", 
                                FileUrl = $"files/{workspaceCalc.Id}/payment_gateway_specs.docx", 
                                FileType = "doc", 
                                FileSize = 1258291, 
                                IsPublic = true, 
                                FederationId = fedId, 
                                CreatedAt = DateTime.UtcNow.AddHours(-1) 
                            };
                            await context.WorkspaceFiles.AddRangeAsync(fedFile1, fedFile2);
                        }

                        await context.SaveChangesAsync();
                        logger.LogInformation("DbInitializer: Federated Workspace records seeded successfully.");
                    }
                }
            }

            // Self-healing: Seed AI R&D Lab channels and messages dynamically if not present
            var wsAI = await context.Workspaces.FirstOrDefaultAsync(w => w.Id == Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444"));
            if (wsAI != null)
            {
                bool needsUpdate = false;

                // Ensure SettingsJson has the correct channels
                if (string.IsNullOrEmpty(wsAI.SettingsJson) || !wsAI.SettingsJson.Contains("ai-models"))
                {
                    logger.LogInformation("DbInitializer: Updating AI R&D Lab settings with channels...");
                    wsAI.SettingsJson = "{\"lockedChannels\":{\"infrastructure\":[\"aaaaaa11-1111-1111-1111-111111111111\",\"bbbbbb22-2222-2222-2222-222222222222\",\"ffffff22-2222-2222-2222-222222222222\"]},\"channelOwners\":{\"ai-models\":\"aaaaaa11-1111-1111-1111-111111111111\",\"infrastructure\":\"bbbbbb22-2222-2222-2222-222222222222\",\"dataset-ops\":\"eeeeee11-1111-1111-1111-111111111111\"},\"channelModerators\":{\"ai-models\":[],\"infrastructure\":[],\"dataset-ops\":[]},\"allChannels\":[\"general\",\"ai-models\",\"infrastructure\",\"dataset-ops\"],\"disabledCreateChannelUsers\":[],\"disabledCreateTaskUsers\":[],\"disabledEditTaskUsers\":[],\"disabledDeleteFileUsers\":[],\"disabledDeleteTaskUsers\":[]}";
                    context.Workspaces.Update(wsAI);
                    needsUpdate = true;
                }

                // Ensure ChatRoom exists for workspaceAI
                var roomId = Guid.Parse("45678901-4567-4567-4567-456789012345");
                var chatRoomAI = await context.ChatRooms.FirstOrDefaultAsync(cr => cr.Id == roomId || cr.WorkspaceId == wsAI.Id);
                if (chatRoomAI == null)
                {
                    logger.LogInformation("DbInitializer: Seeding missing ChatRoom for AI R&D Lab...");
                    chatRoomAI = new ChatRoom { Id = roomId, WorkspaceId = wsAI.Id };
                    await context.ChatRooms.AddAsync(chatRoomAI);
                    needsUpdate = true;
                }
                else
                {
                    roomId = chatRoomAI.Id;
                }

                if (needsUpdate)
                {
                    await context.SaveChangesAsync();
                    needsUpdate = false;
                }

                // Ensure channel messages are seeded
                bool hasChannelMessages = await context.ChatMessages.AnyAsync(m => m.RoomId == roomId && m.Content.Contains("[channel:ai-models]"));
                if (!hasChannelMessages)
                {
                    logger.LogInformation("DbInitializer: Seeding missing channel messages for AI R&D Lab...");
                    
                    var userAlice = await context.Users.Include(u => u.Account).FirstOrDefaultAsync(u => u.Account.Email == "alice@student.edu");
                    var userBob = await context.Users.Include(u => u.Account).FirstOrDefaultAsync(u => u.Account.Email == "bob@student.edu");
                    var userLiam = await context.Users.Include(u => u.Account).FirstOrDefaultAsync(u => u.Account.Email == "liam@student.edu");
                    var userOlivia = await context.Users.Include(u => u.Account).FirstOrDefaultAsync(u => u.Account.Email == "olivia@student.edu");

                    if (userAlice != null && userBob != null && userLiam != null && userOlivia != null)
                    {
                        var newMessages = new List<ChatMessage>
                        {
                            // AI Lab - ai-models Channel
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userAlice.Id, Content = "[channel:ai-models]Let's discuss our model architecture. I'm thinking of starting with a hybrid decoder-only transformer.", SentAt = DateTime.UtcNow.AddHours(-24) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userLiam.Id, Content = "[channel:ai-models]Should we use RoPE for positional embeddings? It seems to perform better at longer context windows.", SentAt = DateTime.UtcNow.AddHours(-23) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userAlice.Id, Content = "[channel:ai-models]Yes, RoPE is a must. Let's target an 8k context length initially.", SentAt = DateTime.UtcNow.AddHours(-22) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userBob.Id, Content = "[channel:ai-models]We'll need to optimize the attention kernel. FlashAttention-2 is integrated into our training stack, so we're good to go.", SentAt = DateTime.UtcNow.AddHours(-21) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userOlivia.Id, Content = "[channel:ai-models]I've updated the model config file in the repository. Let me know if you want to tweak any hyperparameters.", SentAt = DateTime.UtcNow.AddHours(-20) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userAlice.Id, Content = "[channel:ai-models]Great. I'll launch a small 1B param test run tonight to check loss convergence.", SentAt = DateTime.UtcNow.AddHours(-19) },

                            // AI Lab - infrastructure Channel
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userBob.Id, Content = "[channel:infrastructure]H100 node cluster scaling is complete. We now have 8 nodes online (64 GPUs total).", SentAt = DateTime.UtcNow.AddHours(-15) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userOlivia.Id, Content = "[channel:infrastructure]I'm seeing some thermal throttling on node-04 during full load. Can we check the cooling allocation?", SentAt = DateTime.UtcNow.AddHours(-14) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userBob.Id, Content = "[channel:infrastructure]On it. I'll talk to the datacenter team. In the meantime, I set a temporary power limit of 350W on node-04 GPUs.", SentAt = DateTime.UtcNow.AddHours(-13) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userAlice.Id, Content = "[channel:infrastructure]Thanks Bob. Keep me posted. We need the full cluster at 100% capacity for the 70B parameter run next week.", SentAt = DateTime.UtcNow.AddHours(-12) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userBob.Id, Content = "[channel:infrastructure]Good news, the datacenter team verified the airflow blockage. Node-04 is running at normal temperatures now. Power limits restored.", SentAt = DateTime.UtcNow.AddHours(-10) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userOlivia.Id, Content = "[channel:infrastructure]Confirmed. Benchmarks show full throughput without throttling. Cluster is green.", SentAt = DateTime.UtcNow.AddHours(-9) },

                            // AI Lab - dataset-ops Channel
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userLiam.Id, Content = "[channel:dataset-ops]The WebText-filtered dataset is clean. We pruned around 12% of duplicate/low-quality documents.", SentAt = DateTime.UtcNow.AddHours(-18) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userOlivia.Id, Content = "[channel:dataset-ops]Nice work Liam. Did you filter out toxic content and PII?", SentAt = DateTime.UtcNow.AddHours(-17) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userLiam.Id, Content = "[channel:dataset-ops]Yes, ran our default regex filters for PII and used a lightweight classifier for hate speech/NSFW content.", SentAt = DateTime.UtcNow.AddHours(-16) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userAlice.Id, Content = "[channel:dataset-ops]Perfect. What's the final token count for this subset?", SentAt = DateTime.UtcNow.AddHours(-15) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userLiam.Id, Content = "[channel:dataset-ops]About 450 billion tokens. Combined with the code and math datasets, we're looking at a total of 1.2 trillion tokens.", SentAt = DateTime.UtcNow.AddHours(-14) },
                            new ChatMessage { Id = Guid.NewGuid(), RoomId = roomId, SenderId = userBob.Id, Content = "[channel:dataset-ops]Awesome. I'll start pre-staging the data onto the local NVMe cache drives on each GPU node to minimize training latency.", SentAt = DateTime.UtcNow.AddHours(-12) }
                        };
                        await context.ChatMessages.AddRangeAsync(newMessages);
                        await context.SaveChangesAsync();
                    }
                }
            }
        }
    }
}
