using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class PersonalSchedule
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public DateTime? CreatedAt { get; set; }

    public Guid? TaskId { get; set; }

    public virtual Task? Task { get; set; }

    public virtual User User { get; set; } = null!;
}
