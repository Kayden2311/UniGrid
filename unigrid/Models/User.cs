using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class User
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string FullName { get; set; } = null!;

    public string? SubscriptionTier { get; set; }

    public DateTime? SubscriptionExpires { get; set; }

    public string? AvatarUrl { get; set; }

    public string BusinessAttribute { get; set; } = "normal";

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual ICollection<TaskComment> TaskComments { get; set; } = new List<TaskComment>();

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual ICollection<WorkspaceFile> WorkspaceFiles { get; set; } = new List<WorkspaceFile>();

    public virtual ICollection<WorkspaceMember> WorkspaceMembers { get; set; } = new List<WorkspaceMember>();

    public virtual ICollection<Workspace> Workspaces { get; set; } = new List<Workspace>();
}
