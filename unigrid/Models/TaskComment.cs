using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class TaskComment
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public Guid UserId { get; set; }

    public string Content { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual Task Task { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
