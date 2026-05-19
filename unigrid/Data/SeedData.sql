-- UniGrid Seed Data Script
USE [UniGridDb];
GO

-- 1. Create Users
DECLARE @AdminId UNIQUEIDENTIFIER = NEWID();
DECLARE @JohnId UNIQUEIDENTIFIER = NEWID();
DECLARE @JaneId UNIQUEIDENTIFIER = NEWID();

INSERT INTO [Users] ([Id], [Email], [PasswordHash], [FullName], [IsLocked], [CreatedAt])
VALUES 
(@AdminId, 'admin@unigrid.com', 'AQAAAAEAACcQAAAAEAD...', 'System Admin', 0, GETUTCDATE()),
(@JohnId, 'john.doe@example.com', 'AQAAAAEAACcQAAAAEAD...', 'John Doe', 0, GETUTCDATE()),
(@JaneId, 'jane.smith@example.com', 'AQAAAAEAACcQAAAAEAD...', 'Jane Smith', 0, GETUTCDATE());

-- 2. Create Workspaces
DECLARE @WorkspaceSE UNIQUEIDENTIFIER = NEWID();
DECLARE @WorkspaceCalc UNIQUEIDENTIFIER = NEWID();

INSERT INTO [Workspaces] ([Id], [Name], [OwnerId], [JoinCode], [PackageTier], [CreatedAt])
VALUES 
(@WorkspaceSE, 'Software Engineering', @JohnId, 'SE-2024', 'Pro', GETUTCDATE()),
(@WorkspaceCalc, 'Calculus II Study', @JohnId, 'CALC-II', 'Free', GETUTCDATE());

-- 3. Add Members
INSERT INTO [WorkspaceMembers] ([WorkspaceId], [UserId], [Role], [JoinedAt])
VALUES 
(@WorkspaceSE, @JohnId, 'Owner', GETUTCDATE()),
(@WorkspaceSE, @AdminId, 'Member', GETUTCDATE()),
(@WorkspaceSE, @JaneId, 'Member', GETUTCDATE()),
(@WorkspaceCalc, @JohnId, 'Owner', GETUTCDATE()),
(@WorkspaceCalc, @JaneId, 'Member', GETUTCDATE());

-- 4. Create Chat Rooms
INSERT INTO [ChatRooms] ([Id], [WorkspaceId], [CreatedAt])
VALUES 
(NEWID(), @WorkspaceSE, GETUTCDATE()),
(NEWID(), @WorkspaceCalc, GETUTCDATE());

-- 5. Create Tasks
DECLARE @Task1 UNIQUEIDENTIFIER = NEWID();
DECLARE @Task2 UNIQUEIDENTIFIER = NEWID();

INSERT INTO [Tasks] ([Id], [WorkspaceId], [Title], [Description], [AssigneeId], [Status], [Priority], [DueDate], [CreatedAt])
VALUES 
(@Task1, @WorkspaceSE, 'Implement JWT Authentication', 'Secure the backend with JWT tokens.', @AdminId, 1, 3, DATEADD(day, 2, GETUTCDATE()), GETUTCDATE()),
(@Task2, @WorkspaceSE, 'Design System Documentation', 'Complete the Tailwind CSS style guide.', @JaneId, 0, 2, DATEADD(day, 5, GETUTCDATE()), GETUTCDATE());

-- 6. Add Comments
INSERT INTO [TaskComments] ([Id], [TaskId], [UserId], [Content], [CreatedAt])
VALUES 
(NEWID(), @Task1, @JohnId, 'I can help with the middleware configuration!', GETUTCDATE());

PRINT 'Seed data inserted successfully.';
GO
