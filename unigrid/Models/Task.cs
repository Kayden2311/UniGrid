using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class Task
{
    public Guid Id { get; set; }

    public Guid? WorkspaceId { get; set; }

    public Guid? FederationId { get; set; }

    public Guid? AssigneeId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public int? Status { get; set; }

    public int? Priority { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? CreatedAt { get; set; }

    public Guid? CategoryId { get; set; }

    public bool IsCounterTask { get; set; }

    public int TargetCount { get; set; }

    public int CurrentCount { get; set; }

    public virtual User? Assignee { get; set; }

    public virtual TaskCategory? Category { get; set; }

    public virtual ICollection<TaskComment> TaskComments { get; set; } = new List<TaskComment>();

    public virtual Workspace? Workspace { get; set; }

    public virtual WorkspaceFederation? Federation { get; set; }

    public virtual ICollection<WorkspaceFile> WorkspaceFiles { get; set; } = new List<WorkspaceFile>();
}
