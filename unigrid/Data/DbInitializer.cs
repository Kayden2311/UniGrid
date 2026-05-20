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
                    context.Subtasks.RemoveRange(context.Subtasks);
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
                var workspaceWeb = new Workspace { Id = Guid.NewGuid(), Name = "Web Development", OwnerId = userAlice.Id, JoinCode = "WEB-DEV", PackageTier = "Free" };
                var workspaceCalc = new Workspace { Id = Guid.NewGuid(), Name = "Calculus II Study", OwnerId = userBob.Id, JoinCode = "MATH-101", PackageTier = "Free" };

                await context.Workspaces.AddRangeAsync(workspaceSE, workspaceWeb, workspaceCalc);
                await context.SaveChangesAsync();

                // 7. Seed Billings
                var billingSE = new Billing { WorkspaceId = workspaceSE.Id, PackageId = "proplus_monthly", Status = "Active", EndDate = DateTime.UtcNow.AddYears(1) };
                var billingWeb = new Billing { WorkspaceId = workspaceWeb.Id, PackageId = "free_tier", Status = "Active", EndDate = DateTime.UtcNow.AddYears(10) };
                var billingCalc = new Billing { WorkspaceId = workspaceCalc.Id, PackageId = "free_tier", Status = "Active", EndDate = DateTime.UtcNow.AddYears(10) };

                await context.Billings.AddRangeAsync(billingSE, billingWeb, billingCalc);
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
                    new WorkspaceMember { WorkspaceId = workspaceCalc.Id, UserId = userAlice.Id, Role = "Member" }
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

                // 11. Seed Tasks
                var t1 = new unigrid.Models.Task { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), WorkspaceId = workspaceSE.Id, AssigneeId = userAlice.Id, Title = "Design Database Schema", Description = "Create ERD and define all tables for the core schema.", Status = 1, Priority = 3, DueDate = DateTime.UtcNow.AddDays(2) };
                var t2 = new unigrid.Models.Task { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), WorkspaceId = workspaceSE.Id, AssigneeId = userBob.Id, Title = "Setup CI/CD Pipeline", Description = "Configure GitHub Actions for automated building, linting, and testing.", Status = 2, Priority = 3, DueDate = DateTime.UtcNow.AddDays(5) };
                var t3 = new unigrid.Models.Task { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), WorkspaceId = workspaceSE.Id, AssigneeId = userCharlie.Id, Title = "UI Wireframes", Description = "Create wireframes and mockups for all landing, pricing, and app dashboard pages.", Status = 3, Priority = 2, DueDate = DateTime.UtcNow.AddDays(-2) };

                await context.Tasks.AddRangeAsync(t1, t2, t3);
                await context.SaveChangesAsync();

                // 12. Seed Personal Schedules
                await context.PersonalSchedules.AddRangeAsync(
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Calculus Assignment Review", Description = "{\"desc\":\"Finish Calculus Homework sets 4 and 5 with Bob.\",\"priority\":\"medium\",\"color\":2}", StartTime = DateTime.UtcNow.AddHours(4), EndTime = DateTime.UtcNow.AddHours(6) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Weekly Team Sprint Sync", Description = "{\"desc\":\"Sync up database progress and assign new REST API routes.\",\"priority\":\"high\",\"color\":0}", StartTime = DateTime.UtcNow.AddDays(1).AddHours(2), EndTime = DateTime.UtcNow.AddDays(1).AddHours(4) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Preparation for Midterms", Description = "{\"desc\":\"Review Software Engineering concepts and patterns.\",\"priority\":\"medium\",\"color\":1}", StartTime = DateTime.UtcNow.AddDays(3).AddHours(1), EndTime = DateTime.UtcNow.AddDays(3).AddHours(4) },
                    new PersonalSchedule { Id = Guid.NewGuid(), UserId = userAlice.Id, Title = "Database Project Deadline", Description = "{\"desc\":\"Final submission of the fully seeded SQL database script.\",\"priority\":\"high\",\"color\":3}", StartTime = DateTime.UtcNow.AddDays(5).AddHours(5), EndTime = DateTime.UtcNow.AddDays(5).AddHours(7) }
                );
                await context.SaveChangesAsync();

                logger.LogInformation("DbInitializer: Database seeded successfully.");
            }
            else
            {
                logger.LogInformation("DbInitializer: Database is already seeded with Alice. Skipping seeding step.");
                
                // Double-check password for Alice to ensure it's "password123" (prevents local state mismatch issues)
                var aliceAcc = await context.Accounts.FirstOrDefaultAsync(a => a.Email == "alice@student.edu");
                if (aliceAcc != null && aliceAcc.PasswordHash != "password123")
                {
                    logger.LogInformation("DbInitializer: Resetting Alice's password to password123 to match expected defaults.");
                    aliceAcc.PasswordHash = "password123";
                    await context.SaveChangesAsync();
                }
            }
        }
    }
}
