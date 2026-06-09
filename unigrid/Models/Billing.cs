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

    // Custom Transaction Fields
    public decimal? Amount { get; set; }

    public Guid? UserId { get; set; }

    public string? PaymentMethod { get; set; }

    public string? TransactionRef { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Workspace Workspace { get; set; } = null!;
}
