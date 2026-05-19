using System;
using System.Collections.Generic;

namespace unigrid.Models;

public partial class Admin
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string FullName { get; set; } = null!;

    public bool? SuperAdmin { get; set; }

    public virtual Account Account { get; set; } = null!;
}
