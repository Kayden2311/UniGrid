using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class WorkspaceFederation
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string JoinCode { get; set; } = null!;

    public Guid OwnerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User Owner { get; set; } = null!;

    public virtual ICollection<WorkspaceFederationMember> WorkspaceFederationMembers { get; set; } = new List<WorkspaceFederationMember>();

    public virtual ICollection<WorkspaceFile> WorkspaceFiles { get; set; } = new List<WorkspaceFile>();
}
