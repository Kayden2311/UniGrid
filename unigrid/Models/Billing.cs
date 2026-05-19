using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class Billing
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string PackageId { get; set; } = null!;

    public string? Status { get; set; }

    public DateTime EndDate { get; set; }

    public virtual Workspace Workspace { get; set; } = null!;
}
