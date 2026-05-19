using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class Workspace
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string JoinCode { get; set; } = null!;

    public Guid OwnerId { get; set; }

    public string? PackageTier { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<Billing> Billings { get; set; } = new List<Billing>();

    public virtual ChatRoom? ChatRoom { get; set; }

    public virtual User Owner { get; set; } = null!;

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual ICollection<WorkspaceFile> WorkspaceFiles { get; set; } = new List<WorkspaceFile>();

    public virtual ICollection<WorkspaceMember> WorkspaceMembers { get; set; } = new List<WorkspaceMember>();
}
