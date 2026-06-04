using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class WorkspaceFederationMember
{
    public Guid FederationId { get; set; }

    public Guid UserId { get; set; }

    public Guid? PersonalWorkspaceId { get; set; }

    public DateTime JoinedAt { get; set; }

    public string Role { get; set; } = "Member";

    public string Status { get; set; } = "Active";

    public virtual WorkspaceFederation Federation { get; set; } = null!;

    public virtual User User { get; set; } = null!;

    public virtual Workspace? PersonalWorkspace { get; set; }
}
