/* 
   UniGrid Complete Database Schema & Expanded Massive High-Fidelity Seeding Script
   Includes all tables mapped in UniGridDbContext, with extremely realistic, dense seeding.
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
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    RefreshToken VARCHAR(512) NULL,
    RefreshTokenExpiry DATETIME2 NULL
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
    SubscriptionTier NVARCHAR(50) DEFAULT 'Free', -- Free, Pro, ProPlus, Business, Personal
    SubscriptionExpires DATETIME2 NULL,
    AvatarUrl NVARCHAR(MAX) NULL,
    BusinessAttribute NVARCHAR(50) NOT NULL DEFAULT 'normal',
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
    WorkspaceType NVARCHAR(50) NOT NULL DEFAULT 'Personal',
    CompanyName NVARCHAR(256) NULL,
    CompanyTaxCode NVARCHAR(100) NULL,
    CompanyAddress NVARCHAR(500) NULL,
    CONSTRAINT FK_Workspaces_Users FOREIGN KEY (OwnerId) REFERENCES Users(Id)
);

-- 5b. WorkspaceFederations (Enterprise & Academic Federated Groups)
CREATE TABLE WorkspaceFederations (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Name NVARCHAR(256) NOT NULL,
    JoinCode NVARCHAR(20) NOT NULL UNIQUE,
    OwnerId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_WorkspaceFederations_Users FOREIGN KEY (OwnerId) REFERENCES Users(Id)
);

-- 5c. WorkspaceFederationMembers
CREATE TABLE WorkspaceFederationMembers (
    FederationId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    PersonalWorkspaceId UNIQUEIDENTIFIER NOT NULL,
    JoinedAt DATETIME2 DEFAULT GETUTCDATE(),
    PRIMARY KEY (FederationId, UserId),
    CONSTRAINT FK_FedMembers_Federations FOREIGN KEY (FederationId) REFERENCES WorkspaceFederations(Id) ON DELETE CASCADE,
    CONSTRAINT FK_FedMembers_Users FOREIGN KEY (UserId) REFERENCES Users(Id),
    CONSTRAINT FK_FedMembers_Workspaces FOREIGN KEY (PersonalWorkspaceId) REFERENCES Workspaces(Id)
);

-- 6. WorkspaceMembers (RBAC)
CREATE TABLE WorkspaceMembers (
    WorkspaceId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    Role NVARCHAR(50) DEFAULT 'Member',
    JoinedAt DATETIME2 DEFAULT GETUTCDATE(),
    DisplayRole NVARCHAR(100) NULL,
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
    Status INT DEFAULT 0, -- 0: Todo, 1: InProgress, 2: Review, 3: Done
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
    IsPublic BIT NOT NULL DEFAULT 1,
    FederationId UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Files_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id),
    CONSTRAINT FK_Files_Tasks FOREIGN KEY (TaskId) REFERENCES Tasks(Id) ON DELETE SET NULL,
    CONSTRAINT FK_Files_Users FOREIGN KEY (UserId) REFERENCES Users(Id),
    CONSTRAINT FK_Files_WorkspaceFederations FOREIGN KEY (FederationId) REFERENCES WorkspaceFederations(Id) ON DELETE SET NULL
);

-- 11. ChatRooms
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

-- 13. PersonalSchedules
CREATE TABLE PersonalSchedules (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(256) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    StartTime DATETIME2 NOT NULL,
    EndTime DATETIME2 NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    TaskId UNIQUEIDENTIFIER NULL,
    CONSTRAINT FK_PersonalSchedules_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    CONSTRAINT FK_PersonalSchedules_Tasks FOREIGN KEY (TaskId) REFERENCES Tasks(Id) ON DELETE SET NULL
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

-- 16. Notifications
CREATE TABLE Notifications (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId UNIQUEIDENTIFIER NOT NULL,
    Message NVARCHAR(1000) NOT NULL,
    Type NVARCHAR(100) NOT NULL,
    Link NVARCHAR(500) NULL,
    IsRead BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    RelatedId UNIQUEIDENTIFIER NULL,
    CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- 17. WorkspaceInvitations
CREATE TABLE WorkspaceInvitations (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WorkspaceId UNIQUEIDENTIFIER NOT NULL,
    InviterId UNIQUEIDENTIFIER NOT NULL,
    InviteeEmail NVARCHAR(256) NOT NULL,
    Role NVARCHAR(50) NOT NULL DEFAULT 'Member',
    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Invitations_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Invitations_Inviter FOREIGN KEY (InviterId) REFERENCES Users(Id)
);

-- =============================================
-- PERFORMANCE INDEXES
-- =============================================
CREATE INDEX IX_Accounts_Email ON Accounts(Email);
CREATE INDEX IX_Workspaces_JoinCode ON Workspaces(JoinCode);
CREATE INDEX IX_WorkspaceMembers_UserId ON WorkspaceMembers(UserId);
CREATE INDEX IX_Tasks_Status ON Tasks(Status);
CREATE INDEX IX_Tasks_AssigneeId ON Tasks(AssigneeId);
CREATE INDEX IX_Tasks_DueDate ON Tasks(DueDate);
CREATE INDEX IX_PersonalSchedules_UserId ON PersonalSchedules(UserId);
CREATE INDEX IX_ChatMessages_SentAt ON ChatMessages(SentAt);
CREATE INDEX IX_Users_AccountId ON Users(AccountId);
CREATE INDEX IX_Tasks_WorkspaceId ON Tasks(WorkspaceId);
CREATE INDEX IX_WorkspaceFiles_WorkspaceId ON WorkspaceFiles(WorkspaceId);
CREATE INDEX IX_WorkspaceFiles_TaskId ON WorkspaceFiles(TaskId);
CREATE INDEX IX_WorkspaceFiles_FederationId ON WorkspaceFiles(FederationId);
CREATE INDEX IX_TaskComments_TaskId ON TaskComments(TaskId);
CREATE INDEX IX_ChatMessages_RoomId ON ChatMessages(RoomId);

GO

-- =============================================
-- EXPANDED HIGH-FIDELITY SEED DATA (MASSIVE DATASET)
-- =============================================

-- 1. Create Core & Additional Accounts (15 Accounts Total, password is 'password123')
DECLARE @A_Admin UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @A_Mod UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @A_Alice UNIQUEIDENTIFIER = '33333333-3333-3333-3333-333333333333';
DECLARE @A_Bob UNIQUEIDENTIFIER = '44444444-4444-4444-4444-444444444444';
DECLARE @A_Charlie UNIQUEIDENTIFIER = '55555555-5555-5555-5555-555555555555';
DECLARE @A_Diana UNIQUEIDENTIFIER = '66666666-6666-6666-6666-666666666666';
DECLARE @A_Eve UNIQUEIDENTIFIER = '77777777-7777-7777-7777-777777777777';
DECLARE @A_Frank UNIQUEIDENTIFIER = '88888888-7777-6666-5555-444444444444';
DECLARE @A_Grace UNIQUEIDENTIFIER = '99999999-8888-7777-6666-555555555555';
DECLARE @A_Henry UNIQUEIDENTIFIER = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
DECLARE @A_Jack UNIQUEIDENTIFIER = 'bbbbbbbb-cccc-dddd-eeee-ffffffffffff';
DECLARE @A_Kelly UNIQUEIDENTIFIER = 'cccccccc-dddd-eeee-ffff-000000000000';
DECLARE @A_Liam UNIQUEIDENTIFIER = 'dddddddd-eeee-ffff-0000-111111111111';
DECLARE @A_Olivia UNIQUEIDENTIFIER = 'eeeeeeee-ffff-0000-1111-222222222222';
DECLARE @A_Noah UNIQUEIDENTIFIER = 'ffffffff-0000-1111-2222-333333333333';

INSERT INTO Accounts (Id, Email, PasswordHash, Role) VALUES 
(@A_Admin, 'admin@unigrid.com', 'password123', 1),
(@A_Mod, 'mod@unigrid.com', 'password123', 3),
(@A_Alice, 'alice@student.edu', 'password123', 2),
(@A_Bob, 'bob@student.edu', 'password123', 2),
(@A_Charlie, 'charlie@student.edu', 'password123', 2),
(@A_Diana, 'diana@student.edu', 'password123', 2),
(@A_Eve, 'eve@student.edu', 'password123', 2),
(@A_Frank, 'frank@student.edu', 'password123', 2),
(@A_Grace, 'grace@student.edu', 'password123', 2),
(@A_Henry, 'henry@student.edu', 'password123', 2),
(@A_Jack, 'jack@student.edu', 'password123', 2),
(@A_Kelly, 'kelly@student.edu', 'password123', 2),
(@A_Liam, 'liam@student.edu', 'password123', 2),
(@A_Olivia, 'olivia@student.edu', 'password123', 2),
(@A_Noah, 'noah@student.edu', 'password123', 2);

-- 2. Create Profiles
DECLARE @P_Alice UNIQUEIDENTIFIER = 'AAAAAA11-1111-1111-1111-111111111111';
DECLARE @P_Bob UNIQUEIDENTIFIER = 'BBBBBB22-2222-2222-2222-222222222222';
DECLARE @P_Charlie UNIQUEIDENTIFIER = 'CCCCCC33-3333-3333-3333-333333333333';
DECLARE @P_Diana UNIQUEIDENTIFIER = 'DDDDDD44-4444-4444-4444-444444444444';
DECLARE @P_Eve UNIQUEIDENTIFIER = 'EEEEEE55-5555-5555-5555-555555555555';
DECLARE @P_Frank UNIQUEIDENTIFIER = 'FFFFFF66-6666-6666-6666-666666666666';
DECLARE @P_Grace UNIQUEIDENTIFIER = 'AAAAAA77-7777-7777-7777-777777777777';
DECLARE @P_Henry UNIQUEIDENTIFIER = 'BBBBBB88-8888-8888-8888-888888888888';
DECLARE @P_Jack UNIQUEIDENTIFIER = 'CCCCCC99-9999-9999-9999-999999999999';
DECLARE @P_Kelly UNIQUEIDENTIFIER = 'DDDDDD00-0000-0000-0000-000000000000';
DECLARE @P_Liam UNIQUEIDENTIFIER = 'EEEEEE11-1111-1111-1111-111111111111';
DECLARE @P_Olivia UNIQUEIDENTIFIER = 'FFFFFF22-2222-2222-2222-222222222222';
DECLARE @P_Noah UNIQUEIDENTIFIER = 'AAAAAA33-3333-3333-3333-333333333333';

INSERT INTO Admins (AccountId, FullName, SuperAdmin) VALUES (@A_Admin, 'System Administrator', 1);
INSERT INTO Moderators (AccountId, FullName, Region) VALUES (@A_Mod, 'Platform Moderator', 'East-Asia');
INSERT INTO Users (Id, AccountId, FullName, SubscriptionTier, BusinessAttribute) VALUES 
(@P_Alice, @A_Alice, 'Alice Nguyen', 'Business', 'business Include'),
(@P_Bob, @A_Bob, 'Bob Tran', 'Pro', 'normal'),
(@P_Charlie, @A_Charlie, 'Charlie Le', 'ProPlus', 'normal'),
(@P_Diana, @A_Diana, 'Diana Pham', 'Personal', 'normal'),
(@P_Eve, @A_Eve, 'Eve Vu', 'Free', 'normal'),
(@P_Frank, @A_Frank, 'Frank Miller', 'Business', 'business Include'),
(@P_Grace, @A_Grace, 'Grace Hopper', 'Pro', 'normal'),
(@P_Henry, @A_Henry, 'Henry Cavill', 'ProPlus', 'normal'),
(@P_Jack, @A_Jack, 'Jack Dorsey', 'Personal', 'normal'),
(@P_Kelly, @A_Kelly, 'Kelly Clarkson', 'Free', 'normal'),
(@P_Liam, @A_Liam, 'Liam Nguyen', 'Business', 'business Include'),
(@P_Olivia, @A_Olivia, 'Olivia Tran', 'ProPlus', 'normal'),
(@P_Noah, @A_Noah, 'Noah Le', 'Personal', 'normal');

-- 3. Create Expanded Workspaces (11 Workspaces Total)
DECLARE @W_SE UNIQUEIDENTIFIER = '99999999-9999-9999-9999-999999999999';
DECLARE @W_Web UNIQUEIDENTIFIER = '88888888-8888-8888-8888-888888888888';
DECLARE @W_Calc UNIQUEIDENTIFIER = '77777777-7777-7777-7777-777777777777';
DECLARE @W_Physics UNIQUEIDENTIFIER = '66666666-6666-6666-6666-666666666666';
DECLARE @W_English UNIQUEIDENTIFIER = '55555555-5555-5555-5555-555555555555';
DECLARE @W_Research UNIQUEIDENTIFIER = '44444444-4444-4444-4444-444444444444';
DECLARE @W_Design UNIQUEIDENTIFIER = '33333333-2222-1111-0000-999999999999';
DECLARE @W_Mobile UNIQUEIDENTIFIER = '22222222-1111-0000-9999-888888888888';
DECLARE @W_Global UNIQUEIDENTIFIER = '11111111-0000-9999-8888-777777777777';
DECLARE @W_AI UNIQUEIDENTIFIER = 'aaaaaaaa-1111-2222-3333-444444444444';
DECLARE @W_Data UNIQUEIDENTIFIER = 'bbbbbbbb-2222-3333-4444-555555555555';

INSERT INTO Workspaces (Id, Name, OwnerId, JoinCode, PackageTier, WorkspaceType, CompanyName, CompanyTaxCode, CompanyAddress) VALUES 
(@W_SE, 'Enterprise Portal', @P_Alice, 'SE-PRO', 'Business', 'Business', 'UniGrid Corporation', '0109988776', '456 Enterprise Towers, District 1, HCMC'),
(@W_Web, 'E-Commerce Branch', @P_Alice, 'WEB-DEV', 'ProPlus', 'Group', NULL, NULL, NULL),
(@W_Calc, 'Personal Planner', @P_Bob, 'MATH-101', 'Personal', 'Personal', NULL, NULL, NULL),
(@W_Physics, 'Physics Lab', @P_Alice, 'PHYS-101', 'Free', 'Personal', NULL, NULL, NULL),
(@W_English, 'English Composition', @P_Alice, 'ENGL-101', 'Free', 'Personal', NULL, NULL, NULL),
(@W_Research, 'Research Methods', @P_Alice, 'RES-101', 'Free', 'Personal', NULL, NULL, NULL),
(@W_Design, 'UX Design Studio', @P_Bob, 'DSN-FLOW', 'ProPlus', 'Group', NULL, NULL, NULL),
(@W_Mobile, 'Mobile Dev Team', @P_Charlie, 'MBL-APP', 'Pro', 'Group', NULL, NULL, NULL),
(@W_Global, 'Global Corporate Operations', @P_Frank, 'GLB-OPS', 'Business', 'Business', 'Aperture Science', '0991122334', '789 Enrichment Center Rd, Ohio, US'),
(@W_AI, 'AI R&D Lab', @P_Alice, 'AI-LAB', 'ProPlus', 'Group', NULL, NULL, NULL),
(@W_Data, 'Data Analytics Hub', @P_Bob, 'DATA-HUB', 'Pro', 'Group', NULL, NULL, NULL);

-- 4. Set Up Billings for Workspaces
INSERT INTO Billings (WorkspaceId, PackageId, Status, EndDate) VALUES 
(@W_SE, 'business_monthly', 'Active', DATEADD(year, 1, GETUTCDATE())),
(@W_Web, 'proplus_monthly', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_Calc, 'personal_monthly', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_Physics, 'free_tier', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_English, 'free_tier', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_Research, 'free_tier', 'Active', DATEADD(year, 10, GETUTCDATE())),
(@W_Design, 'proplus_monthly', 'Active', DATEADD(year, 5, GETUTCDATE())),
(@W_Mobile, 'pro_monthly', 'Active', DATEADD(year, 5, GETUTCDATE())),
(@W_Global, 'business_monthly', 'Active', DATEADD(year, 2, GETUTCDATE())),
(@W_AI, 'proplus_monthly', 'Active', DATEADD(year, 3, GETUTCDATE())),
(@W_Data, 'pro_monthly', 'Active', DATEADD(year, 3, GETUTCDATE()));

-- 5. Add Workspace Memberships with Nominal Display Roles
INSERT INTO WorkspaceMembers (WorkspaceId, UserId, Role, DisplayRole) VALUES 
-- Enterprise Portal Members
(@W_SE, @P_Alice, 'Manager', 'Head President'), 
(@W_SE, @P_Bob, 'Vice Manager', 'Tech Lead'),
(@W_SE, @P_Charlie, 'Member', 'BA Lead'),
(@W_SE, @P_Diana, 'Member', 'HR Director'),
(@W_SE, @P_Eve, 'Viewer', 'Intern'),
(@W_SE, @P_Frank, 'Member', 'Senior Architect'),
(@W_SE, @P_Grace, 'Member', 'Quality Assurance'),
(@W_SE, @P_Liam, 'Member', 'Lead UI Engineer'),
(@W_SE, @P_Olivia, 'Member', 'DevOps Specialist'),
(@W_SE, @P_Noah, 'Viewer', 'Data Intern'),
-- E-Commerce Members
(@W_Web, @P_Alice, 'Manager', 'Product Owner'),
(@W_Web, @P_Charlie, 'Member', 'Web Developer'),
(@W_Web, @P_Bob, 'Vice Manager', 'Technical Director'),
(@W_Web, @P_Henry, 'Member', 'React Engineer'),
(@W_Web, @P_Liam, 'Member', 'UI Designer'),
-- Personal Planner
(@W_Calc, @P_Bob, 'Manager', 'Student'),
(@W_Calc, @P_Alice, 'Member', 'Tutor'),
-- Physics Lab
(@W_Physics, @P_Alice, 'Manager', 'Researcher'),
(@W_English, @P_Alice, 'Manager', 'Writer'),
(@W_Research, @P_Alice, 'Manager', 'Academic Adviser'),
-- UX Design Studio Members
(@W_Design, @P_Bob, 'Manager', 'UX Lead'),
(@W_Design, @P_Charlie, 'Member', 'UI Designer'),
(@W_Design, @P_Diana, 'Member', 'User Researcher'),
-- Mobile App Dev Team Members
(@W_Mobile, @P_Charlie, 'Manager', 'VP of Engineering'),
(@W_Mobile, @P_Bob, 'Member', 'iOS Lead'),
(@W_Mobile, @P_Henry, 'Member', 'Android Developer'),
(@W_Mobile, @P_Grace, 'Viewer', 'QA Intern'),
-- Global Corporate Operations Members
(@W_Global, @P_Frank, 'Manager', 'VP of Operations'),
(@W_Global, @P_Alice, 'Vice Manager', 'Integration Lead'),
(@W_Global, @P_Jack, 'Member', 'Systems Admin'),
(@W_Global, @P_Kelly, 'Viewer', 'Observer'),
-- AI Lab Members
(@W_AI, @P_Alice, 'Manager', 'AI Principal Researcher'),
(@W_AI, @P_Bob, 'Vice Manager', 'ML Infrastructure Engineer'),
(@W_AI, @P_Liam, 'Member', 'Computer Vision Specialist'),
(@W_AI, @P_Olivia, 'Member', 'Data Operations Lead'),
-- Data Hub Members
(@W_Data, @P_Bob, 'Manager', 'Chief Data Architect'),
(@W_Data, @P_Noah, 'Member', 'Analytics Engineer'),
(@W_Data, @P_Grace, 'Member', 'Statistician');

-- Calculate the current day dynamically in UTC (starts from today)
DECLARE @CurrentMonday DATETIME2 = CAST(GETUTCDATE() AS DATE);

-- 6. Add 50 High-Fidelity Tasks distributed across columns and members
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
DECLARE @T16 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000016';
DECLARE @T17 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000017';
DECLARE @T18 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000018';
DECLARE @T19 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000019';
DECLARE @T20 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000020';
DECLARE @T21 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000021';
DECLARE @T22 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000022';
DECLARE @T23 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000023';
DECLARE @T24 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000024';
DECLARE @T25 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000025';
DECLARE @T26 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000026';
DECLARE @T27 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000027';
DECLARE @T28 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000028';
DECLARE @T29 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000029';
DECLARE @T30 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000030';
DECLARE @T31 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000031';
DECLARE @T32 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000032';
DECLARE @T33 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000033';
DECLARE @T34 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000034';
DECLARE @T35 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000035';
DECLARE @T36 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000036';
DECLARE @T37 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000037';
DECLARE @T38 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000038';
DECLARE @T39 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000039';
DECLARE @T40 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000040';
DECLARE @T41 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000041';
DECLARE @T42 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000042';
DECLARE @T43 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000043';
DECLARE @T44 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000044';
DECLARE @T45 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000045';
DECLARE @T46 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000046';
DECLARE @T47 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000047';
DECLARE @T48 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000048';
DECLARE @T49 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000049';
DECLARE @T50 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000050';

INSERT INTO Tasks (Id, WorkspaceId, AssigneeId, Title, Description, Status, Priority, DueDate) VALUES 
-- Enterprise Portal Tasks (@W_SE)
(@T1, @W_SE, @P_Alice, 'AI Report', 'Generate summary and evaluation of modern transformer models.', 1, 3, DATEADD(minute, 1439, DATEADD(day, 2, @CurrentMonday))),
(@T3, @W_SE, @P_Alice, 'Database Project', 'Seeded SQL relational schema draft submission.', 1, 3, DATEADD(minute, 1439, DATEADD(day, 6, @CurrentMonday))),
(@T7, @W_SE, @P_Bob, 'Setup CI/CD Pipeline', 'Configure GitHub Actions for automated building, linting, and testing.', 2, 3, DATEADD(day, 5, @CurrentMonday)),
(@T8, @W_SE, @P_Eve, 'Deploy to Staging', 'Configure Azure App Service slot deployment for secondary staging testing.', 2, 2, DATEADD(day, 6, @CurrentMonday)),
(@T9, @W_SE, @P_Diana, 'Performance Optimization', 'Minimize bundle sizes and optimize database indexes on active queries.', 0, 1, DATEADD(day, 12, @CurrentMonday)),
(@T10, @W_SE, @P_Charlie, 'Design System Components', 'Assemble beautiful, harmoniously tailored dark mode styled elements.', 1, 2, DATEADD(day, 4, @CurrentMonday)),
(@T11, @W_SE, @P_Bob, 'Database Seeding', 'Compose a denser database seeding script matching the frontend mock data.', 3, 1, DATEADD(day, -2, @CurrentMonday)),
(@T12, @W_SE, @P_Bob, 'Error Handling Middleware', 'Implement a global ExceptionFilter yielding unified JSON error payloads.', 3, 2, DATEADD(day, -1, @CurrentMonday)),
(@T13, @W_SE, @P_Eve, 'File Upload Service', 'Build out custom local or S3 document uploads supporting files tab.', 1, 2, DATEADD(day, 8, @CurrentMonday)),
(@T14, @W_SE, @P_Diana, 'Notification System', 'Send real-time alerts using SignalR and WebSockets upon task actions.', 0, 3, DATEADD(day, 10, @CurrentMonday)),
(@T15, @W_SE, @P_Charlie, 'Landing Page', 'Polish marketing landing page hero gradients and feature carousels.', 3, 1, DATEADD(day, -3, @CurrentMonday)),
(@T16, @W_SE, @P_Frank, 'Architecture Review', 'Review overall structural layer boundaries and clean code guidelines.', 0, 3, NULL),
(@T17, @W_SE, @P_Grace, 'Integrate Unit Tests', 'Write comprehensive unit test fixtures covering business controllers.', 1, 2, DATEADD(day, 3, @CurrentMonday)),
(@T18, @W_SE, @P_Bob, 'GraphQL Gateway Setup', 'Design federation gateway layer resolving queries in microservices.', 2, 3, DATEADD(day, 7, @CurrentMonday)),
(@T19, @W_SE, @P_Charlie, 'Audit Log Implementation', 'Write interceptors saving workspace action audit trails to DB.', 3, 2, DATEADD(day, -5, @CurrentMonday)),
(@T35, @W_SE, @P_Liam, 'Refactor State Management', 'Clean up state mutations and implement centralized store hooks.', 0, 2, DATEADD(day, 9, @CurrentMonday)),
(@T36, @W_SE, @P_Olivia, 'Kubernetes Deployment Config', 'Update Helm charts and ingress configurations for multi-region hosting.', 1, 3, DATEADD(day, 5, @CurrentMonday)),
(@T37, @W_SE, @P_Noah, 'Database Replication Check', 'Review transaction logs, backup validity, and read-replica replication lag.', 3, 1, DATEADD(day, -4, @CurrentMonday)),
(@T38, @W_SE, @P_Alice, 'Corporate Governance Compliance', 'Ensure SOC2 Type II structural compliance checklists are filled.', 0, 3, DATEADD(day, 15, @CurrentMonday)),
(@T39, @W_SE, @P_Frank, 'System Architecture Guide V3', 'Produce extensive software architecture diagrams and design blueprints.', 2, 3, DATEADD(day, 4, @CurrentMonday)),
(@T50, @W_SE, @P_Grace, 'End-to-End Visual Playwright Tests', 'Draft visual snapshot assertion testing files covering UI components.', 1, 2, DATEADD(day, 8, @CurrentMonday)),

-- E-Commerce Branch Tasks (@W_Web)
(@T20, @W_Web, @P_Alice, 'Stripe Checkout', 'Integrate Apple Pay and Stripe Elements in Cart view.', 1, 3, DATEADD(day, 4, @CurrentMonday)),
(@T21, @W_Web, @P_Charlie, 'SEO Optimization', 'Refactor tags, generate sitemaps, and structure schemas for product pages.', 0, 1, NULL),
(@T22, @W_Web, @P_Bob, 'Redis Cache Integration', 'Cache product listings and category nodes under Redis cluster.', 3, 2, DATEADD(day, -2, @CurrentMonday)),
(@T23, @W_Web, @P_Henry, 'React Refactoring', 'Refactor legacy code to functional components with custom hooks.', 1, 2, DATEADD(day, 5, @CurrentMonday)),
(@T40, @W_Web, @P_Liam, 'Cart Checkout Micro-animations', 'Animate cart transitions, item counts, and premium payment checkout flows.', 1, 1, DATEADD(day, 3, @CurrentMonday)),
(@T41, @W_Web, @P_Olivia, 'CDN Edge Cache Tuning', 'Optimize static assets delivery routes and enable Brotli compression.', 3, 2, DATEADD(day, -1, @CurrentMonday)),

-- Personal Planner (@W_Calc)
(@T2, @W_Calc, @P_Alice, 'Math Assignment', 'Solve differential equations and triple integrals problem sets.', 0, 2, DATEADD(minute, 1439, DATEADD(day, 4, @CurrentMonday))),

-- Physics Lab (@W_Physics)
(@T4, @W_Physics, @P_Alice, 'Lab Report #3', 'Calculate absolute error metrics in electric current fields.', 0, 2, DATEADD(minute, 1439, DATEADD(day, 3, @CurrentMonday))),

-- English Composition (@W_English)
(@T5, @W_English, @P_Alice, 'Essay Draft', 'Draft essay arguing for modern architecture paradigms.', 0, 1, DATEADD(minute, 1439, DATEADD(day, 5, @CurrentMonday))),

-- Research Methods (@W_Research)
(@T6, @W_Research, @P_Alice, 'Literature Review', 'Review academic research on adaptive web interfaces.', 1, 3, DATEADD(hour, 18, DATEADD(day, 4, @CurrentMonday))),

-- UX Design Studio Tasks (@W_Design)
(@T24, @W_Design, @P_Bob, 'User Research Synthesis', 'Map affinity diagrams from user interviews and outline core personas.', 1, 3, DATEADD(day, 1, @CurrentMonday)),
(@T25, @W_Design, @P_Charlie, 'Interactive Prototypes', 'Construct complex animated prototype transitions inside Figma.', 0, 2, NULL),
(@T42, @W_Design, @P_Liam, 'Figma Dark Theme Styling', 'Convert core design token overrides to modern sleek slate layouts.', 0, 2, DATEADD(day, 6, @CurrentMonday)),
(@T43, @W_Design, @P_Diana, 'WCAG Accessibility Audit', 'Test screen reader landmarks, contrast ratios, and semantic outlines.', 1, 3, DATEADD(day, 2, @CurrentMonday)),

-- Mobile App Dev Team Tasks (@W_Mobile)
(@T44, @W_Mobile, @P_Noah, 'Mobile Analytics Event Tracking', 'Map custom analytics triggers across user engagement pathways.', 0, 1, DATEADD(day, 14, @CurrentMonday)),
(@T45, @W_Mobile, @P_Henry, 'APNS & FCM Push Notifications', 'Integrate push notification tokens payload parser with notification hub.', 2, 2, DATEADD(day, 7, @CurrentMonday)),

-- Global Corporate Operations (@W_Global)
(@T46, @W_Global, @P_Frank, 'Disaster Recovery Simulation', 'Run active drill testing failovers to secondary geographic data centers.', 1, 3, DATEADD(day, 3, @CurrentMonday)),
(@T47, @W_Global, @P_Jack, 'ISO 27001 Security Audit Prep', 'Collate security incident reports, threat models, and logs.', 2, 2, DATEADD(day, 6, @CurrentMonday)),
(@T48, @W_Global, @P_Kelly, 'Corporate Compliance Briefing', 'Deliver corporate regulatory updates regarding international tax brackets.', 3, 1, DATEADD(day, -5, @CurrentMonday)),
(@T49, @W_Global, @P_Alice, 'Q3 Global Budget Allocation', 'Prepare capital expenditure reports and department financial resources.', 0, 3, DATEADD(day, 11, @CurrentMonday)),

-- AI R&D Lab Tasks (@W_AI)
(@T26, @W_AI, @P_Alice, 'Train Large Language Model', 'Execute multi-node training run of 7B parameter foundation models.', 1, 3, DATEADD(day, 3, @CurrentMonday)),
(@T27, @W_AI, @P_Bob, 'Configure H100 GPU Cluster', 'Set up InfiniBand networking and SLURM workload scheduler configs.', 0, 3, DATEADD(day, 4, @CurrentMonday)),
(@T28, @W_AI, @P_Liam, 'Dataset Curation & Filtering', 'Prune low-quality text tokens and balance instruction tuning records.', 2, 2, DATEADD(day, 1, @CurrentMonday)),
(@T29, @W_AI, @P_Olivia, 'MLOps Deployment Ingress', 'Package optimized model checkpoints inside Triton Server instances.', 3, 2, DATEADD(day, -2, @CurrentMonday)),
(@T30, @W_AI, NULL, 'Quantize Weights for Edge', 'Analyze performance-accuracy trade-offs using 4-bit AWQ compression.', 0, 1, DATEADD(day, 10, @CurrentMonday)),

-- Data Analytics Hub Tasks (@W_Data)
(@T31, @W_Data, @P_Bob, 'ETL Ingestion Pipelines', 'Redesign high-throughput Apache Flink stream ingestion jobs.', 1, 3, DATEADD(day, 2, @CurrentMonday)),
(@T32, @W_Data, @P_Noah, 'Corporate KPI Executive Dashboard', 'Assemble beautiful visual metric charts inside unified executive report.', 0, 2, DATEADD(day, 5, @CurrentMonday)),
(@T33, @W_Data, @P_Grace, 'A/B Test Statistical Analysis', 'Execute chi-square and t-test formulations over conversion ratios.', 2, 2, DATEADD(day, 2, @CurrentMonday)),
(@T34, @W_Data, @P_Bob, 'Migrate to Snowflake Warehouse', 'Port legacy schemas and optimize clustering keys for analytics tables.', 3, 3, DATEADD(day, -3, @CurrentMonday));

-- 8. Add Dynamic, Detailed Task Comments Conversations
INSERT INTO TaskComments (TaskId, UserId, Content, CreatedAt) VALUES 
(@T1, @P_Bob, 'Which transformer models are we focusing on? GPT-4 and Claude 3.5 Sonnet?', DATEADD(hour, -15, GETUTCDATE())),
(@T1, @P_Alice, '@Bob Let''s also include Gemini 1.5 Pro since we are exploring multimodal capabilities.', DATEADD(hour, -14, GETUTCDATE())),
(@T1, @P_Frank, 'I think we should also evaluate Llama 3 for local deployment scenarios, to compare cost vs. latency.', DATEADD(hour, -12, GETUTCDATE())),
(@T1, @P_Bob, 'Good idea. I will compile Llama 3 throughput metrics in the spreadsheet.', DATEADD(hour, -10, GETUTCDATE())),
(@T1, @P_Alice, 'Perfect! Please commit the results to the Files repository when done.', DATEADD(hour, -9, GETUTCDATE())),

(@T3, @P_Bob, 'Seeded DB is set up on local SQL Server. Let me know if anyone runs into FK issues.', DATEADD(hour, -8, GETUTCDATE())),
(@T3, @P_Charlie, 'Awesome! Just tested the seeder, works smoothly on my end.', DATEADD(hour, -7, GETUTCDATE())),
(@T3, @P_Frank, 'Make sure we set up composite indexes on heavily joined tables. It will drastically save execution times.', DATEADD(hour, -5, GETUTCDATE())),

(@T7, @P_Bob, 'Workflow action runs successfully on GitHub. Staging deployment is green.', DATEADD(hour, -10, GETUTCDATE())),
(@T7, @P_Grace, 'I will perform boundary check testing on the auth endpoints today.', DATEADD(hour, -9, GETUTCDATE())),

(@T10, @P_Charlie, 'Just uploaded the Wireframe.png. Please check it out in the Files tab.', DATEADD(hour, -4, GETUTCDATE())),
(@T10, @P_Alice, 'The color palette looks very modern Charlie! Fits the premium theme perfectly.', DATEADD(hour, -3, GETUTCDATE())),
(@T10, @P_Diana, 'Agreed! Very clean spacing and high-fidelity typography.', DATEADD(hour, -2, GETUTCDATE())),
(@T10, @P_Charlie, 'Thanks team! I will start assembling the core component CSS next.', DATEADD(hour, -1, GETUTCDATE())),

(@T26, @P_Liam, 'Training loss is looking extremely good Alice! Settled around 1.15 in epoch 3.', DATEADD(hour, -4, GETUTCDATE())),
(@T26, @P_Alice, 'Fantastic news Liam. Let''s ensure checkpoint files are saved every 500 steps.', DATEADD(hour, -3, GETUTCDATE())),
(@T26, @P_Olivia, 'Triton model storage profiles are ready to ingest the checkpoints as soon as training wraps.', DATEADD(hour, -2, GETUTCDATE())),

(@T31, @P_Noah, 'Ingestion jobs are lagging about 30 seconds behind the event bus. I am scaling up the consumer slots.', DATEADD(hour, -5, GETUTCDATE())),
(@T31, @P_Bob, 'Make sure we increase memory allocations symmetrically. Stream states occupy substantial heap.', DATEADD(hour, -3, GETUTCDATE()));

-- 9. Add Denser Workspace Files (18 Files Total across workspaces)
INSERT INTO WorkspaceFiles (WorkspaceId, TaskId, UserId, FileName, FileUrl, FileType, FileSize) VALUES 
-- SE Portal Files
(@W_SE, @T1, @P_Alice, 'Transformer_Comparison.pdf', 'files/99999999-9999-9999-9999-999999999999/transformer_comparison.pdf', 'pdf', 2516582),
(@W_SE, @T3, @P_Bob, 'Database_Schema_Draft.docx', 'files/99999999-9999-9999-9999-999999999999/db_schema.docx', 'doc', 1153433),
(@W_SE, NULL, @P_Diana, 'Budget.xlsx', 'files/99999999-9999-9999-9999-999999999999/budget.xlsx', 'spreadsheet', 348160),
(@W_SE, @T10, @P_Charlie, 'Wireframe.png', 'files/99999999-9999-9999-9999-999999999999/wireframe.png', 'image', 4404019),
(@W_SE, @T12, @P_Frank, 'Architecture_Spec_V2.pdf', 'files/99999999-9999-9999-9999-999999999999/architecture_spec.pdf', 'pdf', 3145728),
(@W_SE, @T7, @P_Bob, 'CICD_Flowchart.png', 'files/99999999-9999-9999-9999-999999999999/cicd_flow.png', 'image', 1048576),
(@W_SE, NULL, @P_Grace, 'QA_Test_Scenarios.xlsx', 'files/99999999-9999-9999-9999-999999999999/qa_scenarios.xlsx', 'spreadsheet', 524288),
(@W_SE, @T35, @P_Liam, 'Enterprise_UI_Style_Guide.pdf', 'files/99999999-9999-9999-9999-999999999999/ui_style_guide.pdf', 'pdf', 8912896),

-- Web E-Commerce Portal Files
(@W_Web, @T20, @P_Alice, 'Stripe_API_Integration.pdf', 'files/88888888-8888-8888-8888-888888888888/stripe_api.pdf', 'pdf', 1572864),
(@W_Web, NULL, @P_Charlie, 'SEO_Audit_Report.docx', 'files/88888888-8888-8888-8888-888888888888/seo_audit.docx', 'doc', 2097152),
(@W_Web, @T22, @P_Bob, 'Redis_Benchmarking_Results.xlsx', 'files/88888888-8888-8888-8888-888888888888/redis_bench.xlsx', 'spreadsheet', 819200),

-- Design Studio Files
(@W_Design, @T24, @P_Bob, 'User_Personas_Mockup.pdf', 'files/33333333-2222-1111-0000-999999999999/personas.pdf', 'pdf', 4194304),
(@W_Design, NULL, @P_Diana, 'Figma_Export_Assets.zip', 'files/33333333-2222-1111-0000-999999999999/figma_assets.zip', 'zip', 15728640),

-- AI Lab Files
(@W_AI, @T26, @P_Alice, 'Transformer_Weights_V1.bin', 'files/aaaaaaaa-1111-2222-3333-444444444444/transformer_weights.bin', 'binary', 1288490188),
(@W_AI, @T27, @P_Bob, 'GPU_Cluster_Config.yaml', 'files/aaaaaaaa-1111-2222-3333-444444444444/gpu_cluster_config.yaml', 'config', 46080),

-- Data Hub Files
(@W_Data, @T32, @P_Noah, 'ETL_Pipeline_Flow.drawio', 'files/bbbbbbbb-2222-3333-4444-555555555555/etl_pipeline_flow.drawio', 'image', 122880);

-- 10. Add ChatRooms for Workspaces (5 Rooms Total)
DECLARE @CR_SE UNIQUEIDENTIFIER = '12345678-1234-1234-1234-123456789012';
DECLARE @CR_Web UNIQUEIDENTIFIER = '23456789-2345-2345-2345-234567890123';
DECLARE @CR_Design UNIQUEIDENTIFIER = '34567890-3456-3456-3456-345678901234';
DECLARE @CR_AI UNIQUEIDENTIFIER = '45678901-4567-4567-4567-456789012345';
DECLARE @CR_Data UNIQUEIDENTIFIER = '56789012-5678-5678-5678-567890123456';

INSERT INTO ChatRooms (Id, WorkspaceId) VALUES 
(@CR_SE, @W_SE),
(@CR_Web, @W_Web),
(@CR_Design, @W_Design),
(@CR_AI, @W_AI),
(@CR_Data, @W_Data);

-- 11. Add Dozens of Chat Messages inside active Chatrooms
INSERT INTO ChatMessages (RoomId, SenderId, Content, SentAt) VALUES 
-- SE Portal Chat messages
(@CR_SE, @P_Alice, 'Hey everyone! Welcome to our Software Engineering study and workspace group 🎉', DATEADD(hour, -20, GETUTCDATE())),
(@CR_SE, @P_Bob, 'Thanks Alice! Excited to collaborate and get the core database and routes done.', DATEADD(hour, -19, GETUTCDATE())),
(@CR_SE, @P_Charlie, 'I have completed the wireframe mockups! Check the Files tab to download and review.', DATEADD(hour, -18, GETUTCDATE())),
(@CR_SE, @P_Diana, 'Great. I will structure the OpenAPI endpoints according to the wireframes.', DATEADD(hour, -16, GETUTCDATE())),
(@CR_SE, @P_Frank, 'Let''s make sure to stick to the clean architecture folders layout. Saves pain later.', DATEADD(hour, -15, GETUTCDATE())),
(@CR_SE, @P_Grace, 'Unit test suites are mapped out. I will integrate them as soon as Bob commits the CI/CD pipeline.', DATEADD(hour, -14, GETUTCDATE())),
(@CR_SE, @P_Bob, 'CI/CD pipeline is ready! GitHub actions will trigger on every PR now.', DATEADD(hour, -12, GETUTCDATE())),
(@CR_SE, @P_Eve, 'Can you guys give me access to the staging link? Need to test UI views.', DATEADD(hour, -10, GETUTCDATE())),
(@CR_SE, @P_Alice, 'Yes Eve, here it is: https://unigrid-staging.azurewebsites.net', DATEADD(hour, -8, GETUTCDATE())),
(@CR_SE, @P_Eve, 'Got it! Thank you Alice.', DATEADD(hour, -7, GETUTCDATE())),
(@CR_SE, @P_Frank, 'Has anyone optimized the index queries on the AuditLog tables? They are getting a bit slow.', DATEADD(hour, -5, GETUTCDATE())),
(@CR_SE, @P_Bob, 'I did! Created composite indexes on WorkspaceId and Timestamp. Speed is 10x now.', DATEADD(hour, -4, GETUTCDATE())),
(@CR_SE, @P_Liam, 'Refactoring is looking awesome! Added the UI style guide to files.', DATEADD(hour, -3, GETUTCDATE())),
(@CR_SE, @P_Olivia, 'Just upgraded the cluster Helm charts. Deploying to secondary staging now.', DATEADD(hour, -2, GETUTCDATE())),
(@CR_SE, @P_Noah, 'Checked database sync metrics. Replica lag is below 5ms!', DATEADD(hour, -1, GETUTCDATE())),
(@CR_SE, @P_Alice, 'Excellent team effort. Let''s do a quick sync up session this week!', DATEADD(minute, -30, GETUTCDATE())),

-- Web E-Commerce Portal Chat messages
(@CR_Web, @P_Alice, 'Welcome to the E-Commerce Branch channel! Stripe integration is our top priority.', DATEADD(hour, -10, GETUTCDATE())),
(@CR_Web, @P_Charlie, 'I am structuring the product page schemas. Standard JSON-LD is ready.', DATEADD(hour, -8, GETUTCDATE())),
(@CR_Web, @P_Bob, 'Product catalog queries are cached using Redis. Page speeds are below 200ms.', DATEADD(hour, -6, GETUTCDATE())),
(@CR_Web, @P_Henry, 'Refactored Cart view to standard React functional hooks. Check out the latest commit.', DATEADD(hour, -3, GETUTCDATE())),
(@CR_Web, @P_Liam, 'Just polished payment buttons with subtle hover interactions.', DATEADD(hour, -2, GETUTCDATE())),
(@CR_Web, @P_Alice, 'Superb! I will run local testing on Cart payment steps today.', DATEADD(hour, -1, GETUTCDATE())),

-- AI Lab Chat messages
(@CR_AI, @P_Alice, 'Starting active multi-node training loop for our custom LLM!', DATEADD(hour, -8, GETUTCDATE())),
(@CR_AI, @P_Bob, 'InfiniBand is holding up well, no dropped packets reported.', DATEADD(hour, -6, GETUTCDATE())),
(@CR_AI, @P_Liam, 'Instruction dataset is pristine. Trimmed 50k duplicates yesterday.', DATEADD(hour, -4, GETUTCDATE())),
(@CR_AI, @P_Olivia, 'Checked checkpoints folder, autosave is working seamlessly.', DATEADD(hour, -2, GETUTCDATE())),
(@CR_AI, @P_Alice, 'Amazing. Let''s monitor convergence metrics through the weekend.', DATEADD(hour, -1, GETUTCDATE()));

-- 12. Add Dozens of PersonalSchedules Calendar Events (Highly Visual Premium Timeline)
-- Alice Nguyen's dense schedule
INSERT INTO PersonalSchedules (UserId, Title, Description, StartTime, EndTime, TaskId) VALUES 
(@P_Alice, 'Study AI', '{"desc":"Review chapters 5-7","priority":"high","color":0}', DATEADD(hour, 9, @CurrentMonday), DATEADD(hour, 11, @CurrentMonday), NULL),
(@P_Alice, 'Team Meeting', '{"desc":"Sprint review","priority":"medium","color":1}', DATEADD(hour, 11, @CurrentMonday), DATEADD(hour, 12, @CurrentMonday), NULL),
(@P_Alice, 'Gym Workout', '{"desc":"Lower body focus","priority":"low","color":2}', DATEADD(hour, 17, @CurrentMonday), DATEADD(hour, 18, @CurrentMonday), NULL),
(@P_Alice, 'AI Report Session', '{"desc":"Write AI evaluation","priority":"high","color":3}', DATEADD(hour, 13, DATEADD(day, 2, @CurrentMonday)), DATEADD(hour, 15, DATEADD(day, 2, @CurrentMonday)), @T1),
(@P_Alice, 'Database Project Prep', '{"desc":"SQL Schema drafts","priority":"high","color":0}', DATEADD(hour, 10, DATEADD(day, 1, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 1, @CurrentMonday)), @T3),
(@P_Alice, 'Math Practice Prep', '{"desc":"Diff equations practice","priority":"medium","color":3}', DATEADD(hour, 8, DATEADD(day, 4, @CurrentMonday)), DATEADD(hour, 10, DATEADD(day, 4, @CurrentMonday)), @T2),
(@P_Alice, 'Physics Lab Session', '{"desc":"Prepare error analysis charts","priority":"medium","color":1}', DATEADD(hour, 8, DATEADD(day, 2, @CurrentMonday)), DATEADD(minute, 30, DATEADD(hour, 9, DATEADD(day, 2, @CurrentMonday))), @T4),
(@P_Alice, 'Essay Writing Block', '{"desc":"Architecture paradigms essay","priority":"low","color":2}', DATEADD(hour, 14, DATEADD(day, 3, @CurrentMonday)), DATEADD(hour, 16, DATEADD(day, 3, @CurrentMonday)), @T5),
(@P_Alice, 'Literature Review Reading', '{"desc":"Adaptive UI systems review","priority":"high","color":1}', DATEADD(hour, 9, DATEADD(day, 5, @CurrentMonday)), DATEADD(hour, 11, DATEADD(day, 5, @CurrentMonday)), @T6),
(@P_Alice, 'Weekly Alignment sync', '{"desc":"Review active tasks","priority":"low","color":4}', DATEADD(hour, 9, DATEADD(day, 3, @CurrentMonday)), DATEADD(hour, 10, DATEADD(day, 3, @CurrentMonday)), NULL),
(@P_Alice, 'Stripe Payment Integration', '{"desc":"Stripe Apple Pay sandbox","priority":"high","color":0}', DATEADD(hour, 13, DATEADD(day, 4, @CurrentMonday)), DATEADD(hour, 15, DATEADD(day, 4, @CurrentMonday)), @T20),
(@P_Alice, 'LLM Training Supervision', '{"desc":"Assess loss curve checkpoints","priority":"high","color":0}', DATEADD(hour, 10, DATEADD(day, 3, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 3, @CurrentMonday)), @T26);

-- Bob Tran's dense schedule
INSERT INTO PersonalSchedules (UserId, Title, Description, StartTime, EndTime, TaskId) VALUES 
(@P_Bob, 'Setup CI/CD Pipeline Slot', '{"desc":"Action flows","priority":"high","color":3}', DATEADD(hour, 9, DATEADD(day, 1, @CurrentMonday)), DATEADD(hour, 11, DATEADD(day, 1, @CurrentMonday)), @T7),
(@P_Bob, 'GraphQL Gateway Review', '{"desc":"GraphQL resolvers","priority":"high","color":0}', DATEADD(hour, 14, DATEADD(day, 2, @CurrentMonday)), DATEADD(hour, 16, DATEADD(day, 2, @CurrentMonday)), @T18),
(@P_Bob, 'DB index synthesis', '{"desc":"Optimize AuditLog","priority":"medium","color":1}', DATEADD(hour, 10, DATEADD(day, 3, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 3, @CurrentMonday)), NULL),
(@P_Bob, 'Gym Session', '{"desc":"Cardio block","priority":"low","color":2}', DATEADD(hour, 16, DATEADD(day, 4, @CurrentMonday)), DATEADD(hour, 17, @CurrentMonday), NULL),
(@P_Bob, 'GPU Cluster Verification', '{"desc":"Verify InfiniBand state","priority":"high","color":3}', DATEADD(hour, 13, DATEADD(day, 4, @CurrentMonday)), DATEADD(hour, 15, DATEADD(day, 4, @CurrentMonday)), @T27),
(@P_Bob, 'ETL Design Workshop', '{"desc":"Flink task slots design","priority":"medium","color":0}', DATEADD(hour, 10, DATEADD(day, 2, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 2, @CurrentMonday)), @T31);

-- Diana Pham's dense schedule
INSERT INTO PersonalSchedules (UserId, Title, Description, StartTime, EndTime, TaskId) VALUES 
(@P_Diana, 'HR Onboarding Prep', '{"desc":"Study portal profiles","priority":"medium","color":2}', DATEADD(hour, 8, DATEADD(day, 1, @CurrentMonday)), DATEADD(hour, 10, DATEADD(day, 1, @CurrentMonday)), NULL),
(@P_Diana, 'Figma UX Interview analysis', '{"desc":"Synthing affinity diagrams","priority":"high","color":0}', DATEADD(hour, 13, DATEADD(day, 3, @CurrentMonday)), DATEADD(hour, 15, DATEADD(day, 3, @CurrentMonday)), @T24),
(@P_Diana, 'WCAG Review Blocks', '{"desc":"Contrast tests on portal","priority":"medium","color":1}', DATEADD(hour, 14, DATEADD(day, 2, @CurrentMonday)), DATEADD(hour, 16, DATEADD(day, 2, @CurrentMonday)), @T43);

-- Liam Nguyen's schedule
INSERT INTO PersonalSchedules (UserId, Title, Description, StartTime, EndTime, TaskId) VALUES 
(@P_Liam, 'State Management Redesign', '{"desc":"Refactor Redux stores","priority":"high","color":0}', DATEADD(hour, 9, DATEADD(day, 2, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 2, @CurrentMonday)), @T35),
(@P_Liam, 'Micro-animations Drafting', '{"desc":"Framer motion transitions","priority":"low","color":1}', DATEADD(hour, 14, DATEADD(day, 3, @CurrentMonday)), DATEADD(hour, 16, DATEADD(day, 3, @CurrentMonday)), @T40),
(@P_Liam, 'UI tokens alignment', '{"desc":"Dark layouts and buttons","priority":"medium","color":2}', DATEADD(hour, 10, DATEADD(day, 5, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 5, @CurrentMonday)), @T42);

-- Olivia Tran's schedule
INSERT INTO PersonalSchedules (UserId, Title, Description, StartTime, EndTime, TaskId) VALUES 
(@P_Olivia, 'K8s Cluster Upgrade', '{"desc":"Apply ingress and secrets","priority":"high","color":3}', DATEADD(hour, 8, DATEADD(day, 5, @CurrentMonday)), DATEADD(hour, 11, DATEADD(day, 5, @CurrentMonday)), @T36),
(@P_Olivia, 'Triton Setup Block', '{"desc":"Model repository layout","priority":"high","color":0}', DATEADD(hour, 13, DATEADD(day, 1, @CurrentMonday)), DATEADD(hour, 15, DATEADD(day, 1, @CurrentMonday)), @T29),
(@P_Olivia, 'Release Deployment check', '{"desc":"Deploy staging artifact","priority":"low","color":4}', DATEADD(hour, 16, DATEADD(day, 2, @CurrentMonday)), DATEADD(hour, 17, DATEADD(day, 2, @CurrentMonday)), NULL);

-- Noah Le's schedule
INSERT INTO PersonalSchedules (UserId, Title, Description, StartTime, EndTime, TaskId) VALUES 
(@P_Noah, 'Audit Database Replica', '{"desc":"Verify replication streams","priority":"high","color":1}', DATEADD(hour, 10, DATEADD(day, 1, @CurrentMonday)), DATEADD(hour, 12, DATEADD(day, 1, @CurrentMonday)), @T37),
(@P_Noah, 'Analytics Tracking Schema', '{"desc":"Engagement mapping","priority":"low","color":2}', DATEADD(hour, 14, DATEADD(day, 3, @CurrentMonday)), DATEADD(hour, 16, DATEADD(day, 3, @CurrentMonday)), @T44),
(@P_Noah, 'Executive Dashboard Assembly', '{"desc":"Draw charts inside view","priority":"medium","color":0}', DATEADD(hour, 9, DATEADD(day, 4, @CurrentMonday)), DATEADD(hour, 11, DATEADD(day, 4, @CurrentMonday)), @T32);

-- 13. Create three Demo Workspace Federations (Mô hình Liên bang)
DECLARE @Fed_Integration UNIQUEIDENTIFIER = 'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF';
DECLARE @Fed_Academic UNIQUEIDENTIFIER = 'EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE';
DECLARE @Fed_Cloud UNIQUEIDENTIFIER = 'DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD';

INSERT INTO WorkspaceFederations (Id, Name, JoinCode, OwnerId) VALUES
(@Fed_Integration, 'Store Integration Federation', 'FED-STORE', @P_Alice),
(@Fed_Academic, 'Academic Collaboration Alliance', 'FED-ACAD', @P_Bob),
(@Fed_Cloud, 'Cloud Architecture Alliance', 'FED-CLOUD', @P_Bob);

-- 14. Add Members to the Federations
-- Federation 1 members (FED-STORE)
INSERT INTO WorkspaceFederationMembers (FederationId, UserId, PersonalWorkspaceId) VALUES
(@Fed_Integration, @P_Alice, @W_Web),
(@Fed_Integration, @P_Bob, @W_Calc);

-- Federation 2 members (FED-ACAD)
INSERT INTO WorkspaceFederationMembers (FederationId, UserId, PersonalWorkspaceId) VALUES
(@Fed_Academic, @P_Bob, @W_Design),
(@Fed_Academic, @P_Charlie, @W_Mobile);

-- Federation 3 members (FED-CLOUD)
INSERT INTO WorkspaceFederationMembers (FederationId, UserId, PersonalWorkspaceId) VALUES
(@Fed_Cloud, @P_Bob, @W_Calc),
(@Fed_Cloud, @P_Charlie, @W_Mobile),
(@Fed_Cloud, @P_Olivia, @W_Design);

-- 15. Project files to the Federations
-- Project files for FED-STORE
UPDATE WorkspaceFiles SET FederationId = @Fed_Integration, IsPublic = 1 WHERE FileName = 'Transformer_Comparison.pdf';
UPDATE WorkspaceFiles SET FederationId = @Fed_Integration, IsPublic = 1 WHERE FileName = 'Database_Schema_Draft.docx';

-- Projected Files for FED-STORE
INSERT INTO WorkspaceFiles (WorkspaceId, UserId, FileName, FileUrl, FileType, FileSize, IsPublic, FederationId, CreatedAt) VALUES 
(@W_Web, @P_Alice, 'Storefront_Mockups_V1.pdf', 'files/88888888-8888-8888-8888-888888888888/storefront_mockups_v1.pdf', 'pdf', 2202010, 1, @Fed_Integration, DATEADD(hour, -2, GETUTCDATE())),
(@W_Calc, @P_Bob, 'Payment_Gateway_Specs.docx', 'files/77777777-7777-7777-7777-777777777777/payment_gateway_specs.docx', 'doc', 1258291, 1, @Fed_Integration, DATEADD(hour, -1, GETUTCDATE()));

-- Projected Files for FED-ACAD
INSERT INTO WorkspaceFiles (WorkspaceId, UserId, FileName, FileUrl, FileType, FileSize, IsPublic, FederationId, CreatedAt) VALUES 
(@W_Design, @P_Bob, 'Personas_Virt_Export.pdf', 'files/33333333-2222-1111-0000-999999999999/personas_virt.pdf', 'pdf', 3145728, 1, @Fed_Academic, DATEADD(hour, -5, GETUTCDATE())),
(@W_Mobile, @P_Charlie, 'iOS_Architecture_Draft.docx', 'files/22222222-1111-0000-9999-888888888888/ios_arch.docx', 'doc', 1572864, 1, @Fed_Academic, DATEADD(hour, -3, GETUTCDATE()));

-- Projected Files for FED-CLOUD
INSERT INTO WorkspaceFiles (WorkspaceId, UserId, FileName, FileUrl, FileType, FileSize, IsPublic, FederationId, CreatedAt) VALUES 
(@W_AI, @P_Bob, 'GPU_Architecture_Plan.pdf', 'files/aaaaaaaa-1111-2222-3333-444444444444/gpu_arch_plan.pdf', 'pdf', 4194304, 1, @Fed_Cloud, DATEADD(hour, -4, GETUTCDATE())),
(@W_Design, @P_Olivia, 'UI_Dark_Layout_Grid.png', 'files/33333333-2222-1111-0000-999999999999/ui_dark_grid.png', 'image', 1048576, 1, @Fed_Cloud, DATEADD(hour, -3, GETUTCDATE()));

-- 16. Seed Sample Invitations & Notifications (Lively Platform Activities)
INSERT INTO WorkspaceInvitations (WorkspaceId, InviterId, InviteeEmail, Role, Status) VALUES
(@W_AI, @P_Alice, 'grace@student.edu', 'Member', 'Pending'),
(@W_Data, @P_Bob, 'liam@student.edu', 'Member', 'Pending'),
(@W_Web, @P_Alice, 'olivia@student.edu', 'Member', 'Accepted');

INSERT INTO Notifications (UserId, Message, Type, Link, IsRead) VALUES
(@P_Alice, 'You have been appointed Manager of the new AI R&D Lab.', 'WorkspaceInvite', '/workspaces', 0),
(@P_Bob, 'Alice Nguyen assigned you to task: Quantize Weights for Edge.', 'TaskAssignment', '/tasks', 0),
(@P_Liam, 'You have a pending invitation to join: Data Analytics Hub.', 'WorkspaceInvite', '/workspaces', 0),
(@P_Noah, 'Bob Tran assigned you to task: Corporate KPI Executive Dashboard.', 'TaskAssignment', '/tasks', 0);

PRINT 'UniGrid Expanded Massive Database Seeded Successfully.';
GO
