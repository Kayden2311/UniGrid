using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class TaskCategory
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string ColorHex { get; set; } = "#3B82F6";

    public DateTime CreatedAt { get; set; }

    public virtual Workspace Workspace { get; set; } = null!;

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
}
