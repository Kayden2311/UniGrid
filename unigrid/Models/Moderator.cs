using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class Moderator
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Region { get; set; }

    public virtual Account Account { get; set; } = null!;
}
