/* 
   UniGrid Complete Database Schema & High-Fidelity Seeding Script
   Includes all tables mapped in UniGridDbContext, with realistic, dense seeding.
*/

IF EXISTS (SELECT name FROM sys.databases WHERE name = 'UniGridDb')
BEGIN
    ALTER DATABASE UniGridDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE UniGridDb;
END
GO

CREATE DATABASE UniGridDb;
GO

USE UniGridDb;
GO

-- =============================================
-- TABLES DEFINITION
-- =============================================

-- 1. Accounts
CREATE TABLE Accounts (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Email NVARCHAR(256) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(MAX) NOT NULL,
    Role INT NOT NULL, -- 1: Admin, 2: User, 3: Moderator
    IsLocked BIT DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE()
);

-- 2. Admins
CREATE TABLE Admins (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    AccountId UNIQUEIDENTIFIER NOT NULL,
    FullName NVARCHAR(256) NOT NULL,
    SuperAdmin BIT DEFAULT 0,
    CONSTRAINT FK_Admins_Accounts FOREIGN KEY (AccountId) REFERENCES Accounts(Id) ON DELETE CASCADE
);

-- 3. Moderators
CREATE TABLE Moderators (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    AccountId UNIQUEIDENTIFIER NOT NULL,
    FullName NVARCHAR(256) NOT NULL,
    Region NVARCHAR(100) NULL,
    CONSTRAINT FK_Moderators_Accounts FOREIGN KEY (AccountId) REFERENCES Accounts(Id) ON DELETE CASCADE
);

-- 4. Users
CREATE TABLE Users (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    AccountId UNIQUEIDENTIFIER NOT NULL,
    FullName NVARCHAR(256) NOT NULL,
    SubscriptionTier NVARCHAR(50) DEFAULT 'Free', -- Free, Pro, ProPlus, Business
    SubscriptionExpires DATETIME2 NULL,
    AvatarUrl NVARCHAR(MAX) NULL,
    CONSTRAINT FK_Users_Accounts FOREIGN KEY (AccountId) REFERENCES Accounts(Id) ON DELETE CASCADE
);

-- 5. Workspaces
CREATE TABLE Workspaces (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Name NVARCHAR(256) NOT NULL,
    JoinCode NVARCHAR(20) NOT NULL UNIQUE,
    OwnerId UNIQUEIDENTIFIER NOT NULL,
    PackageTier NVARCHAR(50) DEFAULT 'Free',
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Workspaces_Users FOREIGN KEY (OwnerId) REFERENCES Users(Id)
);

-- 6. WorkspaceMembers (RBAC)
CREATE TABLE WorkspaceMembers (
    WorkspaceId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    Role NVARCHAR(50) DEFAULT 'Member',
    JoinedAt DATETIME2 DEFAULT GETUTCDATE(),
    PRIMARY KEY (WorkspaceId, UserId),
    CONSTRAINT FK_Members_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id),
    CONSTRAINT FK_Members_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);

-- 7. Tasks
CREATE TABLE Tasks (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WorkspaceId UNIQUEIDENTIFIER NOT NULL,
    AssigneeId UNIQUEIDENTIFIER NULL,
    Title NVARCHAR(512) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    Status INT DEFAULT 0, -- 0: Todo, 1: InProgress, 2: Review, 3: Done (aligned with 4 Kanban columns)
    Priority INT DEFAULT 1, -- 1: Low, 2: Medium, 3: High
    DueDate DATETIME2 NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Tasks_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id),
    CONSTRAINT FK_Tasks_Users FOREIGN KEY (AssigneeId) REFERENCES Users(Id)
);

-- 9. TaskComments
CREATE TABLE TaskComments (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TaskId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Comments_Tasks FOREIGN KEY (TaskId) REFERENCES Tasks(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Comments_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);

-- 10. WorkspaceFiles
CREATE TABLE WorkspaceFiles (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WorkspaceId UNIQUEIDENTIFIER NOT NULL,
    TaskId UNIQUEIDENTIFIER NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    FileName NVARCHAR(512) NOT NULL,
    FileUrl NVARCHAR(MAX) NOT NULL,
    FileType NVARCHAR(100) NOT NULL,
    FileSize BIGINT NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Files_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id),
    CONSTRAINT FK_Files_Tasks FOREIGN KEY (TaskId) REFERENCES Tasks(Id) ON DELETE SET NULL,
    CONSTRAINT FK_Files_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);

-- 11. ChatRooms (1-to-1 with Workspace as per unique index constraint in EF mapping)
CREATE TABLE ChatRooms (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WorkspaceId UNIQUEIDENTIFIER NOT NULL UNIQUE,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Chat_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id) ON DELETE CASCADE
);

-- 12. ChatMessages
CREATE TABLE ChatMessages (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    RoomId UNIQUEIDENTIFIER NOT NULL,
    SenderId UNIQUEIDENTIFIER NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    SentAt DATETIME2 DEFAULT GETUTCDATE(),
    IsDeleted BIT DEFAULT 0,
    CONSTRAINT FK_Messages_Rooms FOREIGN KEY (RoomId) REFERENCES ChatRooms(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Messages_Users FOREIGN KEY (SenderId) REFERENCES Users(Id)
);

-- 13. PersonalSchedules (Calendar)
CREATE TABLE PersonalSchedules (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(256) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    StartTime DATETIME2 NOT NULL,
    EndTime DATETIME2 NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_PersonalSchedules_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- 14. AuditLogs
CREATE TABLE AuditLogs (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WorkspaceId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    Action NVARCHAR(100) NOT NULL,
    TargetType NVARCHAR(100) NOT NULL,
    TargetId UNIQUEIDENTIFIER NOT NULL,
    Metadata NVARCHAR(MAX) NULL,
    Timestamp DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Audit_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id),
    CONSTRAINT FK_Audit_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);

-- 15. Billings
CREATE TABLE Billings (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WorkspaceId UNIQUEIDENTIFIER NOT NULL,
    PackageId NVARCHAR(100) NOT NULL,
    Status NVARCHAR(50) DEFAULT 'Active',
    EndDate DATETIME2 NOT NULL,
    CONSTRAINT FK_Billing_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id) ON DELETE CASCADE
);

-- =============================================
-- PERFORMANCE INDEXES (Aligned with DbContext & Queries)
-- =============================================
CREATE INDEX IX_Accounts_Email ON Accounts(Email);
CREATE INDEX IX_Workspaces_JoinCode ON Workspaces(JoinCode);
CREATE INDEX IX_WorkspaceMembers_UserId ON WorkspaceMembers(UserId);
CREATE INDEX IX_Tasks_Status ON Tasks(Status);
CREATE INDEX IX_Tasks_AssigneeId ON Tasks(AssigneeId);
CREATE INDEX IX_Tasks_DueDate ON Tasks(DueDate);
CREATE INDEX IX_PersonalSchedules_UserId ON PersonalSchedules(UserId);
CREATE INDEX IX_ChatMessages_SentAt ON ChatMessages(SentAt);

GO

-- =============================================
-- HIGH-FIDELITY SEED DATA
-- =============================================

-- 1. Create Core Accounts (Password is 'password123' for ease of testing)
DECLARE @A_Admin UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @A_Mod UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @A_Alice UNIQUEIDENTIFIER = '33333333-3333-3333-3333-333333333333';
DECLARE @A_Bob UNIQUEIDENTIFIER = '44444444-4444-4444-4444-444444444444';
DECLARE @A_Charlie UNIQUEIDENTIFIER = '55555555-5555-5555-5555-555555555555';
DECLARE @A_Diana UNIQUEIDENTIFIER = '66666666-6666-6666-6666-666666666666';
DECLARE @A_Eve UNIQUEIDENTIFIER = '77777777-7777-7777-7777-777777777777';

INSERT INTO Accounts (Id, Email, PasswordHash, Role) VALUES 
(@A_Admin, 'admin@unigrid.com', 'password123', 1),
(@A_Mod, 'mod@unigrid.com', 'password123', 3),
(@A_Alice, 'alice@student.edu', 'password123', 2),
(@A_Bob, 'bob@student.edu', 'password123', 2),
(@A_Charlie, 'charlie@student.edu', 'password123', 2),
(@A_Diana, 'diana@student.edu', 'password123', 2),
(@A_Eve, 'eve@student.edu', 'password123', 2);

-- 2. Create Profiles
DECLARE @P_Alice UNIQUEIDENTIFIER = 'AAAAAA11-1111-1111-1111-111111111111';
DECLARE @P_Bob UNIQUEIDENTIFIER = 'BBBBBB22-2222-2222-2222-222222222222';
DECLARE @P_Charlie UNIQUEIDENTIFIER = 'CCCCCC33-3333-3333-3333-333333333333';
DECLARE @P_Diana UNIQUEIDENTIFIER = 'DDDDDD44-4444-4444-4444-444444444444';
DECLARE @P_Eve UNIQUEIDENTIFIER = 'EEEEEE55-5555-5555-5555-555555555555';

INSERT INTO Admins (AccountId, FullName, SuperAdmin) VALUES (@A_Admin, 'System Administrator', 1);
INSERT INTO Moderators (AccountId, FullName, Region) VALUES (@A_Mod, 'Platform Moderator', 'East-Asia');
INSERT INTO Users (Id, AccountId, FullName, SubscriptionTier) VALUES 
(@P_Alice, @A_Alice, 'Alice Nguyen', 'ProPlus'),
(@P_Bob, @A_Bob, 'Bob Tran', 'Pro'),
(@P_Charlie, @A_Charlie, 'Charlie Le', 'Free'),
(@P_Diana, @A_Diana, 'Diana Pham', 'Free'),
(@P_Eve, @A_Eve, 'Eve Vu', 'Free');

-- 3. Create Workspaces
DECLARE @W_SE UNIQUEIDENTIFIER = '99999999-9999-9999-9999-999999999999';
DECLARE @W_Web UNIQUEIDENTIFIER = '88888888-8888-8888-8888-888888888888';
DECLARE @W_Calc UNIQUEIDENTIFIER = '77777777-7777-7777-7777-777777777777';
DECLARE @W_Physics UNIQUEIDENTIFIER = '66666666-6666-6666-6666-666666666666';
DECLARE @W_English UNIQUEIDENTIFIER = '55555555-5555-5555-5555-555555555555';
DECLARE @W_Research UNIQUEIDENTIFIER = '44444444-4444-4444-4444-444444444444';

INSERT INTO Workspaces (Id, Name, OwnerId, JoinCode, PackageTier) VALUES 
(@W_SE, 'Software Engineering', @P_Alice, 'SE-PRO', 'ProPlus'),
(@W_Web, 'Web Development', @P_Alice, 'WEB-DEV', 'Free'),
(@W_Calc, 'Calculus II Study', @P_Bob, 'MATH-101', 'Free'),
(@W_Physics, 'Physics Lab', @P_Alice, 'PHYS-101', 'Free'),
(@W_English, 'English Composition', @P_Alice, 'ENGL-101', 'Free'),
(@W_Research, 'Research Methods', @P_Alice, 'RES-101', 'Free');

-- 4. Set Up Billings for workspaces
INSERT INTO Billings (WorkspaceId, PackageId, Status, EndDate) VALUES 
(@W_SE, 'proplus_monthly', 'Active', DATEADD(year, 1, GETUTCDATE())),
(@W_Web, 'free_tier', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_Calc, 'free_tier', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_Physics, 'free_tier', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_English, 'free_tier', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_Research, 'free_tier', 'Active', DATEADD(year, 10, GETUTCDATE()));

-- 5. Add Workspace Memberships (Software Engineering holds all 5 core users)
INSERT INTO WorkspaceMembers (WorkspaceId, UserId, Role) VALUES 
(@W_SE, @P_Alice, 'Owner'), 
(@W_SE, @P_Bob, 'Manager'),
(@W_SE, @P_Charlie, 'Member'),
(@W_SE, @P_Diana, 'Member'),
(@W_SE, @P_Eve, 'Member'),
-- Other memberships
(@W_Web, @P_Alice, 'Owner'),
(@W_Web, @P_Charlie, 'Member'),
(@W_Calc, @P_Bob, 'Owner'),
(@W_Calc, @P_Alice, 'Member'),
(@W_Physics, @P_Alice, 'Owner'),
(@W_English, @P_Alice, 'Owner'),
(@W_Research, @P_Alice, 'Owner');

-- Calculate the current day dynamically in UTC (starts from today)
DECLARE @CurrentMonday DATETIME2 = CAST(GETUTCDATE() AS DATE);

-- 6. Add 15 High-Fidelity Tasks (T1-T6 are synchronized Alice deadlines across Workspaces)
DECLARE @T1 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @T2 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000002';
DECLARE @T3 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000003';
DECLARE @T4 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000004';
DECLARE @T5 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000005';
DECLARE @T6 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000006';
DECLARE @T7 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000007';
DECLARE @T8 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000008';
DECLARE @T9 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000009';
DECLARE @T10 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000010';
DECLARE @T11 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000011';
DECLARE @T12 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000012';
DECLARE @T13 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000013';
DECLARE @T14 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000014';
DECLARE @T15 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000015';

INSERT INTO Tasks (Id, WorkspaceId, AssigneeId, Title, Description, Status, Priority, DueDate) VALUES 
(@T1, @W_SE, @P_Alice, 'AI Report', 'Generate summary and evaluation of modern transformer models.', 1, 3, DATEADD(minute, 1439, DATEADD(day, 2, @CurrentMonday))),
(@T2, @W_Calc, @P_Alice, 'Math Assignment', 'Solve differential equations and triple integrals problem sets.', 0, 2, DATEADD(minute, 1439, DATEADD(day, 4, @CurrentMonday))),
(@T3, @W_SE, @P_Alice, 'Database Project', 'Seeded SQL relational schema draft submission.', 1, 3, DATEADD(minute, 1439, DATEADD(day, 6, @CurrentMonday))),
(@T4, @W_Physics, @P_Alice, 'Lab Report #3', 'Calculate absolute error metrics in electric current fields.', 0, 2, DATEADD(minute, 1439, DATEADD(day, 3, @CurrentMonday))),
(@T5, @W_English, @P_Alice, 'Essay Draft', 'Draft essay arguing for modern architecture paradigms.', 0, 1, DATEADD(minute, 1439, DATEADD(day, 5, @CurrentMonday))),
(@T6, @W_Research, @P_Alice, 'Literature Review', 'Review academic research on adaptive web interfaces.', 1, 3, DATEADD(hour, 18, DATEADD(day, 4, @CurrentMonday))),
-- Other Kanban tasks assigned to other members to preserve visual rich rendering
(@T7, @W_SE, @P_Bob, 'Setup CI/CD Pipeline', 'Configure GitHub Actions for automated building, linting, and testing.', 2, 3, DATEADD(day, 5, @CurrentMonday)),
(@T8, @W_SE, @P_Eve, 'Deploy to Staging', 'Configure Azure App Service slot deployment for secondary staging testing.', 2, 2, DATEADD(day, 6, @CurrentMonday)),
(@T9, @W_SE, @P_Diana, 'Performance Optimization', 'Minimize bundle sizes and optimize database indexes on active queries.', 0, 1, DATEADD(day, 12, @CurrentMonday)),
(@T10, @W_SE, @P_Charlie, 'Design System Components', 'Assemble beautiful, harmoniously tailored dark mode styled elements.', 1, 2, DATEADD(day, 4, @CurrentMonday)),
(@T11, @W_SE, @P_Bob, 'Database Seeding', 'Compose a denser database seeding script matching the frontend mock data.', 3, 1, DATEADD(day, -2, @CurrentMonday)),
(@T12, @W_SE, @P_Bob, 'Error Handling Middleware', 'Implement a global ExceptionFilter yielding unified JSON error payloads.', 3, 2, DATEADD(day, -1, @CurrentMonday)),
(@T13, @W_SE, @P_Eve, 'File Upload Service', 'Build out custom local or S3 document uploads supporting files tab.', 1, 2, DATEADD(day, 8, @CurrentMonday)),
(@T14, @W_SE, @P_Diana, 'Notification System', 'Send real-time alerts using SignalR and WebSockets upon task actions.', 0, 3, DATEADD(day, 10, @CurrentMonday)),
(@T15, @W_SE, @P_Charlie, 'Landing Page', 'Polish marketing landing page hero gradients and feature carousels.', 3, 1, DATEADD(day, -3, @CurrentMonday));

-- 8. Add Task Comments (matching the frontend mockups)
INSERT INTO TaskComments (TaskId, UserId, Content, CreatedAt) VALUES 
(@T1, @P_Bob, 'Which transformer models are we focusing on? GPT-4 and Claude 3.5 Sonnet?', DATEADD(hour, -5, GETUTCDATE())),
(@T1, @P_Alice, '@Bob Let''s also include Gemini 1.5 Pro since we are exploring multimodal capabilities.', DATEADD(hour, -4, GETUTCDATE())),
(@T2, @P_Bob, 'Let me know if you need help with the triple integrals part, I finished those yesterday.', DATEADD(hour, -8, GETUTCDATE())),
(@T5, @P_Eve, 'Make sure to cite the latest papers on modular and clean architecture paradigms.', DATEADD(hour, -10, GETUTCDATE()));

-- 9. Add Workspace Files (matching the frontend files tab)
INSERT INTO WorkspaceFiles (WorkspaceId, TaskId, UserId, FileName, FileUrl, FileType, FileSize) VALUES 
(@W_SE, @T1, @P_Alice, 'Transformer_Comparison.pdf', 'files/transformer_comparison.pdf', 'pdf', 2516582), -- 2.4 MB
(@W_SE, @T3, @P_Bob, 'Database_Schema_Draft.docx', 'files/db_schema.docx', 'doc', 1153433), -- 1.1 MB
(@W_SE, NULL, @P_Diana, 'Budget.xlsx', 'files/budget.xlsx', 'spreadsheet', 348160), -- 340 KB
(@W_SE, @T10, @P_Charlie, 'Wireframe.png', 'files/wireframe.png', 'image', 4404019), -- 4.2 MB
(@W_SE, @T4, @P_Eve, 'Lab_Procedure_3.pdf', 'files/lab_procedure_3.pdf', 'pdf', 911360); -- 890 KB

-- 10. Add ChatRoom for Workspace (General)
DECLARE @CR_SE UNIQUEIDENTIFIER = NEWID();
INSERT INTO ChatRooms (Id, WorkspaceId) VALUES (@CR_SE, @W_SE);

-- 11. Add ChatMessages in general chatroom
INSERT INTO ChatMessages (RoomId, SenderId, Content, SentAt) VALUES 
(@CR_SE, @P_Alice, 'Hey everyone! Welcome to our Software Engineering study and workspace group 🎉', DATEADD(hour, -12, GETUTCDATE())),
(@CR_SE, @P_Bob, 'Thanks Alice! Excited to collaborate and get the core database and routes done.', DATEADD(hour, -11, GETUTCDATE())),
(@CR_SE, @P_Charlie, 'I have completed the wireframe mockups! Check the Files tab to download and review.', DATEADD(hour, -10, GETUTCDATE())),
(@CR_SE, @P_Diana, 'Great. I will structure the OpenAPI endpoints according to the wireframes.', DATEADD(hour, -8, GETUTCDATE())),
(@CR_SE, @P_Alice, 'Excellent team effort. Let''s do a quick sync up session this week!', DATEADD(hour, -2, GETUTCDATE()));

-- 12. Add PersonalSchedules (Personal Calendar events for calendar mapping, synchronized with DbInitializer.cs)
INSERT INTO PersonalSchedules (UserId, Title, Description, StartTime, EndTime) VALUES 
(@P_Alice, 'Study AI', '{"desc":"Review chapters 5-7","priority":"high","color":0}', DATEADD(hour, 9, @CurrentMonday), DATEADD(hour, 11, @CurrentMonday)),
(@P_Alice, 'Team Meeting', '{"desc":"Sprint review","priority":"medium","color":1}', DATEADD(hour, 10, @CurrentMonday), DATEADD(hour, 11, @CurrentMonday)),
(@P_Alice, 'Gym', '{"desc":"","priority":"low","color":2}', DATEADD(hour, 12, DATEADD(day, 2, @CurrentMonday)), DATEADD(minute, 30, DATEADD(hour, 13, DATEADD(day, 2, @CurrentMonday)))),
(@P_Alice, 'Math Practice', '{"desc":"Problem set 6","priority":"medium","color":3}', DATEADD(hour, 8, DATEADD(day, 4, @CurrentMonday)), DATEADD(hour, 10, DATEADD(day, 4, @CurrentMonday))),
(@P_Alice, 'Write Essay', '{"desc":"First draft","priority":"high","color":0}', DATEADD(hour, 10, DATEADD(day, 1, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 1, @CurrentMonday))),
(@P_Alice, 'Physics Lab Prep', '{"desc":"Review procedures","priority":"medium","color":1}', DATEADD(hour, 8, DATEADD(day, 2, @CurrentMonday)), DATEADD(minute, 30, DATEADD(hour, 9, DATEADD(day, 2, @CurrentMonday)))),
(@P_Alice, 'Study Group', '{"desc":"Calculus review","priority":"medium","color":2}', DATEADD(hour, 11, DATEADD(day, 3, @CurrentMonday)), DATEADD(minute, 30, DATEADD(hour, 12, DATEADD(day, 3, @CurrentMonday)))),
(@P_Alice, 'Research Reading', '{"desc":"Papers for lit review","priority":"low","color":4}', DATEADD(hour, 9, DATEADD(day, 5, @CurrentMonday)), DATEADD(minute, 30, DATEADD(hour, 11, DATEADD(day, 5, @CurrentMonday)))),
(@P_Alice, 'Lunch Break', '{"desc":"","priority":"low","color":4}', DATEADD(hour, 11, DATEADD(day, 1, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 1, @CurrentMonday))),
(@P_Alice, 'Code Review', '{"desc":"Review PR #42","priority":"high","color":3}', DATEADD(hour, 8, DATEADD(day, 3, @CurrentMonday)), DATEADD(hour, 9, DATEADD(day, 3, @CurrentMonday)));

PRINT 'UniGrid Complete Dense Database Seeded Successfully.';
GO
