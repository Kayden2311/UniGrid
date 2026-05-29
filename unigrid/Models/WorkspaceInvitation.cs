using System;

namespace unigrid.Models;

public partial class WorkspaceInvitation
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid InviterId { get; set; }

    public string InviteeEmail { get; set; } = null!;

    public string Role { get; set; } = "Member"; // Manager, Member, Viewer

    public string? DisplayRole { get; set; }

    public string Status { get; set; } = "Pending"; // Pending, Accepted, Declined

    public DateTime CreatedAt { get; set; }

    public virtual Workspace Workspace { get; set; } = null!;

    public virtual User Inviter { get; set; } = null!;
}
