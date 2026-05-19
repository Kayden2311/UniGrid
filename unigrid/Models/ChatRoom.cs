using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class ChatRoom
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual Workspace Workspace { get; set; } = null!;
}
