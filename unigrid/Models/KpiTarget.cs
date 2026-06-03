using System;

namespace unigrid.Models;

public partial class KpiTarget
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public Guid CategoryId { get; set; }

    public string PeriodType { get; set; } = null!; // "Daily", "Weekly", "Monthly"

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public int TargetValue { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Workspace Workspace { get; set; } = null!;

    public virtual User User { get; set; } = null!;

    public virtual TaskCategory Category { get; set; } = null!;
}
