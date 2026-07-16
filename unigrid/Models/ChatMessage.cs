using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class ChatMessage
{
    public Guid Id { get; set; }

    public Guid RoomId { get; set; }

    public Guid SenderId { get; set; }

    public string Content { get; set; } = null!;

    public DateTime? SentAt { get; set; }

    public bool? IsDeleted { get; set; }

    public Guid? ParentId { get; set; }

    public virtual ChatRoom Room { get; set; } = null!;

    public virtual User Sender { get; set; } = null!;

    public virtual ChatMessage? Parent { get; set; }

    public virtual ICollection<ChatMessage> Replies { get; set; } = new List<ChatMessage>();
}
