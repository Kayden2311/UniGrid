using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class AuditLog
{
    public Guid Id { get; set; }

    public Guid? WorkspaceId { get; set; }

    public Guid? FederationId { get; set; }

    public Guid UserId { get; set; }

    public string Action { get; set; } = null!;

    public string TargetType { get; set; } = null!;

    public Guid TargetId { get; set; }

    public string? Metadata { get; set; }

    public DateTime? Timestamp { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual Workspace? Workspace { get; set; }

    public virtual WorkspaceFederation? Federation { get; set; }
}
