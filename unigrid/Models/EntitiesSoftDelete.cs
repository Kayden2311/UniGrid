using System;

namespace unigrid.Models
{
    public partial class Account { public bool IsDisabled { get; set; } = false; }
    public partial class Admin { public bool IsDisabled { get; set; } = false; }
    public partial class Moderator { public bool IsDisabled { get; set; } = false; }
    public partial class User { public bool IsDisabled { get; set; } = false; }
    public partial class WorkspaceFederation { public bool IsDisabled { get; set; } = false; }
    public partial class Workspace { public bool IsDisabled { get; set; } = false; }
    public partial class WorkspaceFederationMember { public bool IsDisabled { get; set; } = false; }
    public partial class WorkspaceMember { public bool IsDisabled { get; set; } = false; }
    public partial class TaskCategory { public bool IsDisabled { get; set; } = false; }
    public partial class Task { public bool IsDisabled { get; set; } = false; }
    public partial class KpiTarget { public bool IsDisabled { get; set; } = false; }
    public partial class TaskComment { public bool IsDisabled { get; set; } = false; }
    public partial class WorkspaceFile { public bool IsDisabled { get; set; } = false; }
    public partial class ChatRoom { public bool IsDisabled { get; set; } = false; }
    public partial class ChatMessage { public bool IsDisabled { get; set; } = false; }
    public partial class PersonalSchedule { public bool IsDisabled { get; set; } = false; }
    public partial class AuditLog { public bool IsDisabled { get; set; } = false; }
    public partial class Billing { public bool IsDisabled { get; set; } = false; }
    public partial class Notification { public bool IsDisabled { get; set; } = false; }
    public partial class WorkspaceInvitation { public bool IsDisabled { get; set; } = false; }
    public partial class SystemSetting { public bool IsDisabled { get; set; } = false; }
}
