using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class WorkspaceMember
{
    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public string? Role { get; set; }

    public DateTime? JoinedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual Workspace Workspace { get; set; } = null!;
}
