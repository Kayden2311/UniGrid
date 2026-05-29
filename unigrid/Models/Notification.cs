using System;

namespace unigrid.Models;

public partial class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Message { get; set; } = null!;

    public string Type { get; set; } = null!; // Invitation, TaskAssignment, TaskStatusChange

    public string? Link { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? RelatedId { get; set; }

    public virtual User User { get; set; } = null!;
}
