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
                            CONSTRAINT [FK_WorkspaceFederations_Users] FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[Users]([Id])
                        );
                    END
                ");

                // B. Check and create WorkspaceFederationMembers
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WorkspaceFederationMembers]') AND type in (N'U'))
                    BEGIN
                        CREATE TABLE [dbo].[WorkspaceFederationMembers] (
                            [FederationId] UNIQUEIDENTIFIER NOT NULL,
                            [UserId] UNIQUEIDENTIFIER NOT NULL,
                            [PersonalWorkspaceId] UNIQUEIDENTIFIER NOT NULL,
                            [JoinedAt] DATETIME2 DEFAULT GETUTCDATE(),
                            PRIMARY KEY ([FederationId], [UserId]),
                            CONSTRAINT [FK_FedMembers_Federations] FOREIGN KEY ([FederationId]) REFERENCES [dbo].[WorkspaceFederations]([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_FedMembers_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
                            CONSTRAINT [FK_FedMembers_Workspaces] FOREIGN KEY ([PersonalWorkspaceId]) REFERENCES [dbo].[Workspaces]([Id])
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
                    context.AuditLogs.RemoveRange(context.AuditLogs);
                    context.WorkspaceFiles.RemoveRange(context.WorkspaceFiles);
                    context.TaskComments.RemoveRange(context.TaskComments);
                    context.PersonalSchedules.RemoveRange(context.PersonalSchedules);
                    context.Tasks.RemoveRange(context.Tasks);
                    context.ChatMessages.RemoveRange(context.ChatMessages);
                    context.ChatRooms.RemoveRange(context.ChatRooms);
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

                // 4. Seed Accounts
                var accAdmin = new Account { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Email = "admin@unigrid.com", PasswordHash = "password123", Role = 1 };
                var accMod = new Account { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Email = "mod@unigrid.com", PasswordHash = "password123", Role = 3 };
                var accAlice = new Account { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Email = "alice@student.edu", PasswordHash = "password123", Role = 2 };
                var accBob = new Account { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Email = "bob@student.edu", PasswordHash = "password123", Role = 2 };
                var accCharlie = new Account { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Email = "charlie@student.edu", PasswordHash = "password123", Role = 2 };
                var accDiana = new Account { Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), Email = "diana@student.edu", PasswordHash = "password123", Role = 2 };
                var accEve = new Account { Id = Guid.Parse("77777777-7777-7777-7777-777777777777"), Email = "eve@student.edu", PasswordHash = "password123", Role = 2 };

                await context.Accounts.AddRangeAsync(accAdmin, accMod, accAlice, accBob, accCharlie, accDiana, accEve);
                await context.SaveChangesAsync();

                // 5. Seed Profiles
                var profileAdmin = new Admin { AccountId = accAdmin.Id, FullName = "System Administrator", SuperAdmin = true };
                var profileMod = new Moderator { AccountId = accMod.Id, FullName = "Platform Moderator", Region = "East-Asia" };

                var userAlice = new User { Id = Guid.Parse("AAAAAA11-1111-1111-1111-111111111111"), AccountId = accAlice.Id, FullName = "Alice Nguyen", SubscriptionTier = "ProPlus" };
                var userBob = new User { Id = Guid.Parse("BBBBBB22-2222-2222-2222-222222222222"), AccountId = accBob.Id, FullName = "Bob Tran", SubscriptionTier = "Pro" };
                var userCharlie = new User { Id = Guid.Parse("CCCCCC33-3333-3333-3333-333333333333"), AccountId = accCharlie.Id, FullName = "Charlie Le", SubscriptionTier = "Free" };
                var userDiana = new User { Id = Guid.Parse("DDDDDD44-4444-4444-4444-444444444444"), AccountId = accDiana.Id, FullName = "Diana Pham", SubscriptionTier = "Free" };
                var userEve = new User { Id = Guid.Parse("EEEEEE55-5555-5555-5555-555555555555"), AccountId = accEve.Id, FullName = "Eve Vu", SubscriptionTier = "Free" };

                await context.Admins.AddAsync(profileAdmin);
                await context.Moderators.AddAsync(profileMod);
                await context.Users.AddRangeAsync(userAlice, userBob, userCharlie, userDiana, userEve);
                await context.SaveChangesAsync();

                // 6. Seed Workspaces
                var workspaceSE = new Workspace { Id = Guid.Parse("99999999-9999-9999-9999-999999999999"), Name = "Software Engineering", OwnerId = userAlice.Id, JoinCode = "SE-PRO", PackageTier = "ProPlus" };
                var workspaceWeb = new Workspace { Id = Guid.Parse("88888888-8888-8888-8888-888888888888"), Name = "Web Development", OwnerId = userAlice.Id, JoinCode = "WEB-DEV", PackageTier = "Personal" };
                var workspaceCalc = new Workspace { Id = Guid.Parse("77777777-7777-7777-7777-777777777777"), Name = "Calculus II Study", OwnerId = userBob.Id, JoinCode = "MATH-101", PackageTier = "Personal" };
                var workspacePhysics = new Workspace { Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), Name = "Physics Lab", OwnerId = userAlice.Id, JoinCode = "PHYS-101", PackageTier = "Free" };
                var workspaceEnglish = new Workspace { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Name = "English Composition", OwnerId = userAlice.Id, JoinCode = "ENGL-101", PackageTier = "Free" };
                var workspaceResearch = new Workspace { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "Research Methods", OwnerId = userAlice.Id, JoinCode = "RES-101", PackageTier = "Free" };

                await context.Workspaces.AddRangeAsync(workspaceSE, workspaceWeb, workspaceCalc, workspacePhysics, workspaceEnglish, workspaceResearch);
                await context.SaveChangesAsync();

                // 7. Seed Billings
                var billingSE = new Billing { WorkspaceId = workspaceSE.Id, PackageId = "proplus_monthly", Status = "Active", EndDate = DateTime.UtcNow.AddYears(1) };
                var billingWeb = new Billing { WorkspaceId = workspaceWeb.Id, PackageId = "personal_monthly", Status = "Active", EndDate = DateTime.UtcNow.AddYears(10) };
                var billingCalc = new Billing { WorkspaceId = workspaceCalc.Id, PackageId = "personal_monthly", Status = "Active", EndDate = DateTime.UtcNow.AddYears(10) };
                var billingPhysics = new Billing { WorkspaceId = workspacePhysics.Id, PackageId = "free_tier", Status = "Active", EndDate = DateTime.UtcNow.AddYears(10) };
                var billingEnglish = new Billing { WorkspaceId = workspaceEnglish.Id, PackageId = "free_tier", Status = "Active", EndDate = DateTime.UtcNow.AddYears(10) };
                var billingResearch = new Billing { WorkspaceId = workspaceResearch.Id, PackageId = "free_tier", Status = "Active", EndDate = DateTime.UtcNow.AddYears(10) };

                await context.Billings.AddRangeAsync(billingSE, billingWeb, billingCalc, billingPhysics, billingEnglish, billingResearch);
                await context.SaveChangesAsync();

                // 8. Seed Members
                await context.WorkspaceMembers.AddRangeAsync(
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userAlice.Id, Role = "Owner" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userBob.Id, Role = "Manager" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userCharlie.Id, Role = "Member" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userDiana.Id, Role = "Member" },
                    new WorkspaceMember { WorkspaceId = workspaceSE.Id, UserId = userEve.Id, Role = "Member" },
                    new WorkspaceMember { WorkspaceId = workspaceWeb.Id, UserId = userAlice.Id, Role = "Owner" },
                    new WorkspaceMember { WorkspaceId = workspaceWeb.Id, UserId = userCharlie.Id, Role = "Member" },
                    new WorkspaceMember { WorkspaceId = workspaceCalc.Id, UserId = userBob.Id, Role = "Owner" },
                    new WorkspaceMember { WorkspaceId = workspaceCalc.Id, UserId = userAlice.Id, Role = "Member" },
                    new WorkspaceMember { WorkspaceId = workspacePhysics.Id, UserId = userAlice.Id, Role = "Owner" },
                    new WorkspaceMember { WorkspaceId = workspaceEnglish.Id, UserId = userAlice.Id, Role = "Owner" },
                    new WorkspaceMember { WorkspaceId = workspaceResearch.Id, UserId = userAlice.Id, Role = "Owner" }
                );
                await context.SaveChangesAsync();

                // 9. Seed ChatRooms
                var crSE = new ChatRoom { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id };
                var crWeb = new ChatRoom { Id = Guid.NewGuid(), WorkspaceId = workspaceWeb.Id };
                var crCalc = new ChatRoom { Id = Guid.NewGuid(), WorkspaceId = workspaceCalc.Id };

                await context.ChatRooms.AddRangeAsync(crSE, crWeb, crCalc);
                await context.SaveChangesAsync();

                // 10. Seed ChatMessages
                await context.ChatMessages.AddRangeAsync(
                    new ChatMessage { RoomId = crSE.Id, SenderId = userAlice.Id, Content = "Hey everyone! Welcome to our Software Engineering study and workspace group 🥳", SentAt = DateTime.UtcNow.AddHours(-12) },
                    new ChatMessage { RoomId = crSE.Id, SenderId = userBob.Id, Content = "Thanks Alice! Excited to collaborate and get the core database and routes done.", SentAt = DateTime.UtcNow.AddHours(-11) }
                );
                await context.SaveChangesAsync();

                // Set the base date starting from today dynamically
                var currentMonday = DateTime.UtcNow.Date;

                // 11. Seed Tasks (Synchronized with React Schedule.tsx deadlines)
                var t1 = new unigrid.Models.Task { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, AssigneeId = userAlice.Id, Title = "AI Report", Description = "Generate summary and evaluation of modern transformer models.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(2).AddHours(23).AddMinutes(59) };
                var t2 = new unigrid.Models.Task { Id = Guid.NewGuid(), WorkspaceId = workspaceCalc.Id, AssigneeId = userAlice.Id, Title = "Math Assignment", Description = "Solve differential equations and triple integrals problem sets.", Status = 0, Priority = 2, DueDate = currentMonday.AddDays(4).AddHours(23).AddMinutes(59) };
                var t3 = new unigrid.Models.Task { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, AssigneeId = userAlice.Id, Title = "Database Project", Description = "Seeded SQL relational schema draft submission.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(6).AddHours(23).AddMinutes(59) };
                var t4 = new unigrid.Models.Task { Id = Guid.NewGuid(), WorkspaceId = workspacePhysics.Id, AssigneeId = userAlice.Id, Title = "Lab Report #3", Description = "Calculate absolute error metrics in electric current fields.", Status = 0, Priority = 2, DueDate = currentMonday.AddDays(3).AddHours(23).AddMinutes(59) };
                var t5 = new unigrid.Models.Task { Id = Guid.NewGuid(), WorkspaceId = workspaceEnglish.Id, AssigneeId = userAlice.Id, Title = "Essay Draft", Description = "Draft essay arguing for modern architecture paradigms.", Status = 0, Priority = 1, DueDate = currentMonday.AddDays(5).AddHours(23).AddMinutes(59) };
                var t6 = new unigrid.Models.Task { Id = Guid.NewGuid(), WorkspaceId = workspaceResearch.Id, AssigneeId = userAlice.Id, Title = "Literature Review", Description = "Review academic research on adaptive web interfaces.", Status = 1, Priority = 3, DueDate = currentMonday.AddDays(4).AddHours(18) };

                await context.Tasks.AddRangeAsync(t1, t2, t3, t4, t5, t6);
                await context.SaveChangesAsync();

                // 12. Seed Personal Schedules (Synchronized with React Schedule.tsx initialTasks)
                await context.PersonalSchedules.AddRangeAsync(
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Study AI", Description = "{\"desc\":\"Review chapters 5-7\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddHours(9), EndTime = currentMonday.AddHours(11) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Team Meeting", Description = "{\"desc\":\"Sprint review\",\"priority\":\"medium\",\"color\":1}", StartTime = currentMonday.AddHours(10), EndTime = currentMonday.AddHours(11) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Gym", Description = "{\"desc\":\"\",\"priority\":\"low\",\"color\":2}", StartTime = currentMonday.AddDays(2).AddHours(12), EndTime = currentMonday.AddDays(2).AddHours(13).AddMinutes(30) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Math Practice", Description = "{\"desc\":\"Problem set 6\",\"priority\":\"medium\",\"color\":3}", StartTime = currentMonday.AddDays(4).AddHours(8), EndTime = currentMonday.AddDays(4).AddHours(10) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Write Essay", Description = "{\"desc\":\"First draft\",\"priority\":\"high\",\"color\":0}", StartTime = currentMonday.AddDays(1).AddHours(10), EndTime = currentMonday.AddDays(1).AddHours(12) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Physics Lab Prep", Description = "{\"desc\":\"Review procedures\",\"priority\":\"medium\",\"color\":1}", StartTime = currentMonday.AddDays(2).AddHours(8), EndTime = currentMonday.AddDays(2).AddHours(9).AddMinutes(30) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Study Group", Description = "{\"desc\":\"Calculus review\",\"priority\":\"medium\",\"color\":2}", StartTime = currentMonday.AddDays(3).AddHours(11), EndTime = currentMonday.AddDays(3).AddHours(12).AddMinutes(30) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Research Reading", Description = "{\"desc\":\"Papers for lit review\",\"priority\":\"low\",\"color\":4}", StartTime = currentMonday.AddDays(5).AddHours(9), EndTime = currentMonday.AddDays(5).AddHours(11).AddMinutes(30) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Lunch Break", Description = "{\"desc\":\"\",\"priority\":\"low\",\"color\":4}", StartTime = currentMonday.AddDays(1).AddHours(11), EndTime = currentMonday.AddDays(1).AddHours(12) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Code Review", Description = "{\"desc\":\"Review PR #42\",\"priority\":\"high\",\"color\":3}", StartTime = currentMonday.AddDays(3).AddHours(8), EndTime = currentMonday.AddDays(3).AddHours(9) }
                );
                await context.SaveChangesAsync();

                // 13. Seed Workspace Files (matching the frontend files tab)
                var file1 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, TaskId = t1.Id, UserId = userAlice.Id, FileName = "Transformer_Comparison.pdf", FileUrl = $"files/{workspaceSE.Id}/transformer_comparison.pdf", FileType = "pdf", FileSize = 2516582, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-4) };
                var file2 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, TaskId = t3.Id, UserId = userBob.Id, FileName = "Database_Schema_Draft.docx", FileUrl = $"files/{workspaceSE.Id}/db_schema.docx", FileType = "doc", FileSize = 1153433, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-3) };
                var file3 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, UserId = userDiana.Id, FileName = "Budget.xlsx", FileUrl = $"files/{workspaceSE.Id}/budget.xlsx", FileType = "spreadsheet", FileSize = 348160, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-2) };
                var file4 = new WorkspaceFile { Id = Guid.NewGuid(), WorkspaceId = workspaceSE.Id, UserId = userCharlie.Id, FileName = "Wireframe.png", FileUrl = $"files/{workspaceSE.Id}/wireframe.png", FileType = "image", FileSize = 4404019, IsPublic = true, CreatedAt = DateTime.UtcNow.AddHours(-1) };

                await context.WorkspaceFiles.AddRangeAsync(file1, file2, file3, file4);
                await context.SaveChangesAsync();

                // 14. Seed Workspace Federations (Mô hình Liên bang)
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
                await context.SaveChangesAsync();

                // 15. Seed Workspace Federation Members
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
                await context.SaveChangesAsync();

                // 16. Seed projected files inside member personal workspaces that map/project to the Federation portal
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
        }
    }
}
