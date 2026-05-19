# UniGrid Project Context & Specification

## 1. Project Overview & Problem Statement
**UniGrid** is a lightweight, unified Workspace and Team Management platform custom-built for students, academic clubs, and small startup teams (3–10 members).

### The Problem
Teams currently suffer from fragmented workflows: loose tasks scattered across instant messengers (Messenger, Zalo), disjointed documentation (Google Docs, Notion), vague task assignments, and isolated deadlines that fail to sync with individual schedules. This leads to a total loss of tracking, accountability issues ("free-riders"), and missed deadlines.

### The Solution
UniGrid bridges these gaps by consolidating all team operations into a singular, high-visibility stream:
* **Centralized Workspace:** Aggregates all team-related components into one shared workspace.
* **Clear Accountability:** Enforces Explicit Assignee $\rightarrow$ Specific Deadline $\rightarrow$ Real-time Status workflows.
* **Native Calendar Sync:** Automatically projects team deadlines onto each member's personal calendar—solving the core adoption bottleneck found in traditional tools.
* **Transparent Metrics:** Tracks real-time progress and contribution to eliminate "forgotten tasks" and unequal workloads.

---

## 2. Target Audience
* **Students:** Managing group assignments, term projects, or thesis cohorts.
* **Small Startups:** Teams of 3–10 people requiring high agility without enterprise bloat.
* **Clubs & Study Groups:** Organizations needing a collaborative space lighter than Jira, but more structured and flow-oriented than Notion or Trello.

---

## 3. System Actors
1.  **Guest:** Unauthenticated user. Can view the landing page, register an account, and log in.
2.  **User (Member):** Authenticated user. Joins workspaces via code/invite, manages personal tasks, views personal/group calendars, uploads files, comments on tasks, and participates in the Workspace group chat.
3.  **Workspace Owner (Leader):** A User with elevated administrative rights over a specific workspace. Can create workspaces, manage membership (invite/remove), assign tasks, configure boards, track team progress, and upgrade workspace packages.
4.  **Admin (System):** Back-office administrator. Manages users, workspaces, billing packages, moderates global content, monitors transactions, and handles account lock/unlock states.
5.  **Payment User:** Any User/Leader executing financial transactions to upgrade or renew workspace tiers.
6.  **Payment Gateway (External System):** External payment service handler. Processes payment requests, returns transaction statuses, and fires verification callbacks to UniGrid.

---

## 4. Core Features & System Architecture

### A. Account Management
* Secure Registration / Login / Password recovery.
* User Profile and preference management.

### B. Workspace Architecture
* Dynamic Workspace creation.
* Invitation system via secure, short-lived alpha-numeric join codes.
* Role-Based Access Control (RBAC): `Owner` vs. `Member`.

### C. Task Engine (Core Component)
* **Attributes:** Title, Description, Assignee, Strict Deadline, Priority Level (`Low`, `Medium`, `High`), Attachments.
* **State Machine:** `Todo` $\rightarrow$ `Doing` $\rightarrow$ `Done`.
* Interactive task commenting system and file attachments.

### D. Kanban Board Interaction
* Visual Column-based layout representing task states.
* Drag-and-drop state mutations.
* Quick filters (e.g., Filter by Assignee).

### E. Native Calendar & Deadline Sync (The Competitive Differentiator)
* Dual-layer calendar: Personal Schedule + Shared Group Deadlines.
* **Automated Injection:** Assigning a task inside a group workspace automatically propagates that deadline directly into the assignee's personal calendar interface.

### F. Activity Tracking
* Immutable audit log capturing task modifications (`Actor`, `Action`, `Target`, `Timestamp`).

### G. Workspace File Repository
* Centralized cloud storage for shared files alongside context-specific task attachments.

### H. Chat Engine (MVP Specifications)
To maintain velocity and systemic stability during the MVP phase, the chat architecture operates under strict constraints:
* **Scope:** Exactly **one (1) centralized Group Chat Room per Workspace** (`WorkspaceId` $\leftrightarrow$ `RoomId`). No direct messages (DMs), no task-isolated sub-threads.
* **Tech Stack Flow:** Real-time communications powered by WebSockets/SignalR.

---

## 5. Frontend Architecture & Components
The **UniGrid** frontend is built using **React + Vite + Tailwind CSS**, following a component-driven architecture with premium aesthetics (Glassmorphism, Vibrant HSL palettes).

### A. Layout & Navigation
* **AppLayout:** Persistent navigation shell.
* **AppSidebar:** Quick access to Dashboard, Workspace List, Schedule, and Pricing.
* **AppHeader:** Global search, Notification center, and User profile menu.

### B. Core Pages
* **Dashboard:** Personal overview with Task Filters, Monthly Progress Metrics, and a Mini Calendar view.
* **Schedule:** Full-page interactive calendar with personal and group deadline integration.
* **WorkspaceDetail:** Multi-tab view containing:
    * **Dashboard Tab:** Metric cards (In Progress, Overdue), Contribution Charts (Pie/Bar/Area), and Recent Activity.
    * **Tasks Tab:** Interactive Kanban Board with Drag-and-Drop, Status Columns (Todo, In Progress, Review, Done), and Task Detail Dialogs.
    * **Chat Tab:** Real-time workspace communication.
    * **Members Tab:** RBAC management (Invite/Remove) and role assignment.
    * **Files Tab:** Shared workspace repository.

---

## 6. Database Schema & Requirements
To support the "Loveable Frontend" and core logic, the database is structured as follows:

### Core Entities
| Table | Description | Key Fields |
| :--- | :--- | :--- |
| **Users** | Identity & Profiles | `Id`, `Email`, `PasswordHash`, `FullName`, `AvatarUrl`, `IsLocked` |
| **Workspaces**| Shared team spaces | `Id`, `Name`, `JoinCode`, `OwnerId`, `PackageTier` |
| **WorkspaceMembers**| Membership & Roles | `WorkspaceId`, `UserId`, `Role` (Owner/Member) |
| **Tasks** | Unit of work | `Id`, `WorkspaceId`, `AssigneeId`, `Status` (Todo/InProgress/Review/Done), `Priority` (Low/Med/High), `DueDate` |
| **Subtasks** | Granular task items | `Id`, `TaskId`, `Content`, `IsDone` |
| **TaskComments**| Collaborative feedback| `Id`, `TaskId`, `UserId`, `Content` (supports @mentions) |
| **TaskDependencies**| Blocking logic | `TaskId`, `DependsOnTaskId` |
| **WorkspaceFiles**| Cloud repository | `Id`, `WorkspaceId`, `TaskId` (optional), `FileUrl`, `FileType` |

### Interaction & Audit
| Table | Description | Key Fields |
| :--- | :--- | :--- |
| **ChatRooms** | 1:1 Workspace link | `Id`, `WorkspaceId` |
| **ChatMessages**| Real-time logs | `Id`, `RoomId`, `SenderId`, `Content`, `SentAt` |
| **AuditLogs** | Activity Tracking | `Id`, `WorkspaceId`, `UserId`, `Action` (Move/Complete/Assign), `TargetId`, `Timestamp` |
| **Billing** | Subscription states | `Id`, `WorkspaceId`, `PackageId`, `Status`, `EndDate` |

---

| ID | Actor | Use Case Description |
| :--- | :--- | :--- |
| **G1–G4** | **Guest** | View Landing / Register Account / Login / Forgot Password |
| **U1–U2** | **User** | Join Workspace via Code / View Joined Workspaces |
| **U3–U5** | **User** | View Assigned Tasks / Update Task Status / View Kanban Board |
| **U6–U8** | **User** | Chat in Workspace Group / Receive Push Notifications / Update Profile |
| **L1–L4** | **Leader** | Create Workspace / Manage Workspace Info / Invite Members / Remove Members |
| **L5–L7** | **Leader** | Create Task / Assign Task / Edit or Delete Task |
| **L8–L11**| **Leader** | Track Team Progress / Manage Workspace Chat / Upgrade Package / View Statistics |
| **A1–A6** | **Admin** | Manage Users, Workspaces, Packages / Monitor Payments / Moderate Content / Account Lock |
| **P1–P4** | **Payment User** | View Tiers / Purchase Package / View Billing History / Renew Package |
| **PG1–PG3**| **Gateway (Ext)**| Process Payment Request / Return Status / Send Webhook Callback |

---

## 8. Advanced Roadmap & Assistive Intelligence
* **Personal Productivity:** Academic timetable integration, Overload Warnings, Sub-task checklists.
* **Collaboration:** Chat-to-Task Conversion, Task dependencies, Workload balance tracking.
* **Rule-Based Logic:** Automated daily prioritization engine based on proximity to deadline and individual bandwidth.

---

## 9. Product-Market Differentiation
* **vs. Google Calendar:** Links individual timelines directly to team ecosystems.
* **vs. Trello / Jira:** Lower enterprise overhead than Jira; integrated communication unlike Trello.
* **vs. Notion:** Opinionated execution flow instead of an unstructured canvas.