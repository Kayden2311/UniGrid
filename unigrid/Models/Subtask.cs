using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class Subtask
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public string Content { get; set; } = null!;

    public bool? IsDone { get; set; }

    public virtual Task Task { get; set; } = null!;
}
