# ✨ UniGrid — Unified Workspace & Team Management

[![Framework: .NET Core](https://img.shields.io/badge/Framework-.NET%20Core%2010.0-blueviolet?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![Database: SQL Server](https://img.shields.io/badge/Database-SQL%20Server-red?style=for-the-badge&logo=microsoft-sql-server)](https://www.microsoft.com/sql-server)
[![UI: Tailwind CSS](https://img.shields.io/badge/Styling-Tailwind%20CSS-blue?style=for-the-badge&logo=tailwind-css)](https://tailwindcss.com/)
[![Interactions: Alpine.js](https://img.shields.io/badge/Interactions-Alpine.js-turquoise?style=for-the-badge&logo=alpine.js)](https://alpinejs.dev/)

> **A premium, high-visibility Workspace and Task Management platform designed specifically for students, academic clubs, and small startup teams (3–10 members).**

---

## 📖 Table of Contents
1. [Project Overview](#-project-overview)
2. [Target Audience & System Actors](#-target-audience--system-actors)
3. [Key Features](#-key-features)
4. [Tech Stack Architecture](#-tech-stack-architecture)
5. [Database Schema](#-database-schema)
6. [Getting Started & Local Setup](#-getting-started--local-setup)
7. [Security & gitignore Configuration](#-security--gitignore-configuration)

---

## 🚀 Project Overview

**UniGrid** addresses the fragmentation that teams experience when working across scattered task sheets, isolated Google Docs, and messy messaging channels (Messenger, Zalo). It consolidates tasks, deadlines, shared files, and real-time chat into **one highly loveable workspace interface**, solving accountability issues and missing timeline visibility.

### 💡 The Competitive Edge
* **Centralized Workspaces:** Combines Kanban boards, repository storage, and real-time SignalR chat rooms.
* **Native Deadline Sync:** Assigning a task inside a workspace automatically propagates that deadline directly into the assignee's personal dashboard calendar.
* **Transparent Contribution Metrics:** Computes real-time workspace member completion rates, helping prevent unequal workloads and the "free-rider" effect.
* **Vibrant Glassmorphic Aesthetics:** Built using premium colors, soft HSL gradients, and reactive hover animations.

---

## 👥 Target Audience & System Actors

### Target Audience
* **Students:** Managing semester-long group work, research circles, or thesis cohorts.
* **Small Startups:** Agile teams of 3–10 people demanding high speed without enterprise weight.
* **Student Clubs:** Teams looking for a setup lighter than Jira but more structured and action-oriented than Notion.

### Core Actors
1. **Guest:** Unauthenticated user. Can view the landing layout, read pricing comparisons, and register/login.
2. **User (Member):** Authenticated collaborator. Joins workspaces via secure codes, acts on assigned tasks, registers schedules, and chats.
3. **Workspace Owner (Leader):** Created elevated workspaces, manages membership, creates/assigns tasks, and controls board reviews.
4. **Admin (System):** Manages users, locking configurations, Billing packages, and global transactions.

---

## 🛠️ Key Features

### 📅 Native Calendar Sync & Dashboard
* Core metric indicators summarizing total, overdue, and pending workloads.
* Personalized calendar dynamically overlaying team deadlines onto personal events.

### 📋 Interactive task engine & Kanban Board
* 4-state workflow board: `Todo` ➔ `In Progress` ➔ `Review` ➔ `Done`.
* Dialog triggers displaying checkable checklists (subtasks), markdown descriptions, and collaborative comment feeds.
* Elevates workspace leaders to review task completions, supporting **Approve & Close** or **Request Rework** pathways.

### 💬 Real-Time Group Chat Room
* Powered by ASP.NET Core SignalR for real-time messaging updates.
* Restricted to exactly one group chat (`#general`) per Workspace to maximize focus and maintain MVP agility.

### 📂 Workspace File Repository
* Shared document repository categorizing PDF Specs, Documents, Spreadsheets, and Image Assets.
* Tracks storage metrics against standard billing limits, providing file details and click-to-download controls.

---

## 💻 Tech Stack Architecture

UniGrid implements a unified, server-rendered **SPA-hybrid** architecture leveraging Razor Pages and client-side reactive components for maximum performance and fluid transitions:

* **Backend:** ASP.NET Core 10.0 (Razor Pages), Entity Framework Core (EF Core).
* **Database:** Microsoft SQL Server (LocalDB / Express).
* **Real-time Channels:** ASP.NET Core SignalR (WebSockets fallback).
* **Frontend Scripting:** Alpine.js (State management, tab systems, calendar nodes).
* **Styling & Assets:** Vanilla CSS, Tailwind CSS CDN integration, Lucide Icons.

---

## 📊 Database Schema

UniGrid utilizes a highly normalized SQL Server relational database, populated with rich seed data:

### Core Tables
| Table | Description | Key Fields |
| :--- | :--- | :--- |
| **Accounts** | Core security authentication identities | `Id`, `Email`, `PasswordHash`, `Role` (Admin/User/Mod) |
| **Users** | Core profiles of registered users | `Id`, `AccountId`, `FullName`, `SubscriptionTier` (Free/Pro/ProPlus) |
| **Workspaces** | Team collabs | `Id`, `Name`, `JoinCode`, `OwnerId`, `PackageTier` |
| **WorkspaceMembers**| Membership map | `WorkspaceId`, `UserId`, `Role` (Owner/Manager/Member) |
| **Tasks** | Assignable deliverables | `Id`, `WorkspaceId`, `AssigneeId`, `Status` (0-3), `Priority` (1-3) |
| **Subtasks** | Checklist granularity | `Id`, `TaskId`, `Content`, `IsDone` |
| **TaskComments** | Collaborative feedback | `Id`, `TaskId`, `UserId`, `Content`, `CreatedAt` |
| **WorkspaceFiles** | Shared documents | `Id`, `WorkspaceId`, `UserId`, `FileName`, `FileType`, `FileSize` |
| **ChatRooms** | 1:1 Workspace room map | `Id`, `WorkspaceId` |
| **ChatMessages** | Group timeline logs | `Id`, `RoomId`, `SenderId`, `Content`, `SentAt` |
| **PersonalSchedules**| Individual calendar dates | `Id`, `UserId`, `Title`, `EventDate` |

---

## ⚙️ Getting Started & Local Setup

### Prerequisites
* **.NET SDK 10.0** or higher
* **Microsoft SQL Server** (LocalDB or Express)
* **Visual Studio / VS Code** (with C# extensions)

### 1. Database Initialization & Seeding
1. Open your SQL Server terminal or management studio (SSMS).
2. Connect to your database engine (default connection uses `localhost` with SQL authentication).
3. Execute the dense mock seeding script located at:
   ```bash
   sqlcmd -S localhost -U sa -P 123 -i ../UniGridDB.sql
   ```
   *(Note: This seeds 5 core users (Alice, Bob, Charlie, Diana, Eve) with the password `password123`, 3 collaborative workspaces, 15 high-priority tasks, schedules, chat logs, and files).*

### 2. Configure the Backend Connection
Ensure your connection strings in the project folder [appsettings.json](file:///e:/FPT/SE%20Semester%208/EXE201/UniGrid/unigrid/unigrid/appsettings.json) are properly configured:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=UniGridDb;User ID=sa;Password=123;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

### 3. Restore and Run
Navigate into the inner project workspace and execute the project:
```bash
cd unigrid
dotnet restore
dotnet run
```
Access the application on: **`http://localhost:5181`**

### 🧪 Standard Login Credentials (Testing)
* **Email:** `alice@student.edu`
* **Password:** `password123`

---

## 🔒 Security & gitignore Configuration

To protect database files, development environment secrets, and passwords, a robust `.gitignore` file has been added to the folder.

It guarantees that the following elements are **never** committed to version control:
* `.vs/` & `.vscode/` (user-specific workspace environments)
* `bin/` & `obj/` (compiled binary builds)
* `*.mdf` & `*.ldf` (active SQL Server attached database files)
* `secrets.json`, `appsettings.local.json` & `.env` (local secret vaults)

> [!WARNING]
> Always use environment variables or **User Secrets (`dotnet user-secrets`)** when deploying UniGrid to staging/production. Never hardcode passwords or production database credentials inside `appsettings.json`.
