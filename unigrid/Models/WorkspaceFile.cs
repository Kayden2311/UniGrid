using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class WorkspaceFile
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid? TaskId { get; set; }

    public Guid UserId { get; set; }

    public string FileName { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public string FileType { get; set; } = null!;

    public long FileSize { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Task? Task { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual Workspace Workspace { get; set; } = null!;
}
