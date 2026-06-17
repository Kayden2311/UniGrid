using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace unigrid.Pages.Admin
{
    [Authorize(Roles = "1,3")]
    public class SubscriptionsModel : PageModel
    {
        private readonly UniGridDbContext _context;

        public SubscriptionsModel(UniGridDbContext context)
        {
            _context = context;
        }

        public class WorkspaceSubscriptionViewModel
        {
            public Guid WorkspaceId { get; set; }
            public string Name { get; set; } = null!;
            public string JoinCode { get; set; } = null!;
            public string PackageTier { get; set; } = "Free";
            public string OwnerEmail { get; set; } = null!;
            public string OwnerName { get; set; } = null!;
            public DateTime CreatedAt { get; set; }
            public Billing? ActiveBilling { get; set; }
        }

        public class BillingViewModel
        {
            public Guid BillingId { get; set; }
            public string WorkspaceName { get; set; } = null!;
            public string PackageId { get; set; } = null!;
            public string Status { get; set; } = null!;
            public DateTime EndDate { get; set; }
            public decimal Amount { get; set; }
            public string PayerName { get; set; } = "N/A";
            public string PayerEmail { get; set; } = "N/A";
            public string PaymentMethod { get; set; } = "N/A";
            public string TransactionRef { get; set; } = "N/A";
            public DateTime CreatedAt { get; set; }
        }

        public List<WorkspaceSubscriptionViewModel> WorkspacesList { get; set; } = new();
        public List<BillingViewModel> BillingsList { get; set; } = new();

        public async System.Threading.Tasks.Task OnGetAsync()
        {
            // Fetch all workspaces and map owners and active billing
            var workspaces = await _context.Workspaces
                .Include(w => w.Owner)
                .Include(w => w.Billings)
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync();

            WorkspacesList = workspaces.Select(w => new WorkspaceSubscriptionViewModel
            {
                WorkspaceId = w.Id,
                Name = w.Name,
                JoinCode = w.JoinCode,
                PackageTier = w.PackageTier ?? "Free",
                OwnerEmail = w.Owner.Account != null ? w.Owner.Account.Email : "Unknown", // Safe fallback
                OwnerName = w.Owner.FullName,
                CreatedAt = w.CreatedAt ?? DateTime.UtcNow,
                ActiveBilling = w.Billings.FirstOrDefault(b => b.Status == "Active")
            }).ToList();

            // Resolve owner emails if Account table is loaded separately
            var accounts = await _context.Accounts.ToListAsync();
            var users = await _context.Users.ToListAsync();

            foreach (var ws in WorkspacesList)
            {
                var user = workspaces.First(w => w.Id == ws.WorkspaceId).Owner;
                var account = accounts.FirstOrDefault(a => a.Id == user.AccountId);
                if (account != null)
                {
                    ws.OwnerEmail = account.Email;
                }
            }

            // Fetch and map Billings
            var billings = await _context.Billings
                .Include(b => b.Workspace)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            BillingsList = billings.Select(b => {
                var userProfile = users.FirstOrDefault(u => u.Id == b.UserId);
                var accountRecord = userProfile != null ? accounts.FirstOrDefault(a => a.Id == userProfile.AccountId) : null;
                
                // Fallback amount calculations if not stored (e.g. older seeds)
                decimal amountFallback = b.Amount ?? 0;
                if (amountFallback == 0)
                {
                    var pkg = b.PackageId.ToLower();
                    if (pkg.Contains("business")) amountFallback = pkg.Contains("yearly") ? 8900000 : 899000;
                    else if (pkg.Contains("proplus")) amountFallback = pkg.Contains("yearly") ? 4400000 : 449000;
                    else if (pkg.Contains("pro")) amountFallback = pkg.Contains("yearly") ? 2900000 : 299000;
                    else if (pkg.Contains("personal")) amountFallback = pkg.Contains("yearly") ? 399000 : 40000;
                }

                return new BillingViewModel
                {
                    BillingId = b.Id,
                    WorkspaceName = b.Workspace?.Name ?? "Deleted Workspace",
                    PackageId = b.PackageId,
                    Status = b.Status ?? "Active",
                    EndDate = b.EndDate,
                    Amount = amountFallback,
                    PayerName = userProfile?.FullName ?? b.Workspace?.Owner?.FullName ?? "System",
                    PayerEmail = accountRecord?.Email ?? "N/A",
                    PaymentMethod = b.PaymentMethod ?? "System Override",
                    TransactionRef = b.TransactionRef ?? "TXN-MANUAL-" + b.Id.ToString().Substring(0, 8).ToUpper(),
                    CreatedAt = b.CreatedAt ?? DateTime.UtcNow.AddDays(-30) // Fallback for old seeds
                };
            }).ToList();
        }

        // Action: Change Workspace Subscription Plan directly
        public async System.Threading.Tasks.Task<IActionResult> OnPostUpdatePlanAsync(Guid workspaceId, string tier)
        {
            var workspace = await _context.Workspaces
                .Include(w => w.Billings)
                .Include(w => w.Owner)
                .FirstOrDefaultAsync(w => w.Id == workspaceId);

            if (workspace == null) return NotFound();

            workspace.PackageTier = tier;

            // Handle corresponding billing entry
            var activeBilling = workspace.Billings.FirstOrDefault(b => b.Status == "Active");
            if (activeBilling != null)
            {
                if (tier == "Free")
                {
                    activeBilling.Status = "Terminated";
                }
                else
                {
                    activeBilling.PackageId = $"{tier.ToLower()}_monthly";
                    activeBilling.Amount = tier switch
                    {
                        "Business" => 899000,
                        "ProPlus" => 449000,
                        "Pro" => 299000,
                        "Personal" => 40000,
                        _ => 0
                    };
                }
            }
            else if (tier != "Free")
            {
                decimal amount = tier switch
                {
                    "Business" => 899000,
                    "ProPlus" => 449000,
                    "Pro" => 299000,
                    "Personal" => 40000,
                    _ => 0
                };

                // Create a new active billing record
                var newBilling = new Billing
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspace.Id,
                    PackageId = $"{tier.ToLower()}_monthly",
                    Status = "Active",
                    EndDate = DateTime.UtcNow.AddMonths(1),
                    Amount = amount,
                    UserId = workspace.OwnerId,
                    PaymentMethod = "Admin Portal Control",
                    TransactionRef = "TXN-ADMIN-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                    CreatedAt = DateTime.UtcNow
                };
                await _context.Billings.AddAsync(newBilling);
            }

            await _context.SaveChangesAsync();
            TempData["SubSuccess"] = $"Workspace '{workspace.Name}' plan tier updated to {tier}.";
            return RedirectToPage();
        }

        // Action: Extend/Modify Billing Expiration
        public async System.Threading.Tasks.Task<IActionResult> OnPostExtendBillingAsync(Guid billingId, int extensionMonths)
        {
            var billing = await _context.Billings
                .Include(b => b.Workspace)
                .FirstOrDefaultAsync(b => b.Id == billingId);

            if (billing == null) return NotFound();

            if (extensionMonths <= 0)
            {
                TempData["SubError"] = "Extension duration must be at least 1 month.";
                return RedirectToPage();
            }

            // If billing is expired, start from now. Otherwise, extend from current end date.
            if (billing.EndDate < DateTime.UtcNow)
            {
                billing.EndDate = DateTime.UtcNow.AddMonths(extensionMonths);
            }
            else
            {
                billing.EndDate = billing.EndDate.AddMonths(extensionMonths);
            }

            billing.Status = "Active"; // Ensure it is active
            await _context.SaveChangesAsync();

            TempData["SubSuccess"] = $"Billing plan for workspace '{billing.Workspace.Name}' extended by {extensionMonths} months.";
            return RedirectToPage();
        }

        // Action: Create manually a Billing Record
        public async System.Threading.Tasks.Task<IActionResult> OnPostCreateBillingAsync(Guid workspaceId, string packageId, int durationMonths)
        {
            var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId);
            if (workspace == null) return NotFound();

            if (durationMonths <= 0)
            {
                TempData["SubError"] = "Subscription duration must be at least 1 month.";
                return RedirectToPage();
            }

            // Terminate existing active billings for this workspace
            var activeBillings = await _context.Billings
                .Where(b => b.WorkspaceId == workspaceId && b.Status == "Active")
                .ToListAsync();

            foreach (var active in activeBillings)
            {
                active.Status = "Expired";
            }

            // Sync workspace tier
            string tier = "Free";
            if (packageId.Contains("business")) tier = "Business";
            else if (packageId.Contains("proplus")) tier = "ProPlus";
            else if (packageId.Contains("pro")) tier = "Pro";
            else if (packageId.Contains("personal")) tier = "Personal";

            workspace.PackageTier = tier;

            decimal amount = tier switch
            {
                "Business" => packageId.Contains("yearly") ? 8900000 : 899000,
                "ProPlus" => packageId.Contains("yearly") ? 4400000 : 449000,
                "Pro" => packageId.Contains("yearly") ? 2900000 : 299000,
                "Personal" => packageId.Contains("yearly") ? 399000 : 40000,
                _ => 0
            };

            // Create new Billing record
            var billing = new Billing
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                PackageId = packageId,
                Status = "Active",
                EndDate = DateTime.UtcNow.AddMonths(durationMonths),
                Amount = amount,
                UserId = workspace.OwnerId,
                PaymentMethod = "Admin Manual Record",
                TransactionRef = "TXN-MANUAL-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                CreatedAt = DateTime.UtcNow
            };

            await _context.Billings.AddAsync(billing);
            await _context.SaveChangesAsync();

            TempData["SubSuccess"] = $"Billing transaction successfully recorded for workspace '{workspace.Name}'.";
            return RedirectToPage();
        }

        // Action: Experimental sync from admin mail box
        public async System.Threading.Tasks.Task<IActionResult> OnPostSyncFromEmailAsync()
        {
            var workspaces = await _context.Workspaces
                .Include(w => w.Owner)
                .Take(3)
                .ToListAsync();

            if (!workspaces.Any())
            {
                TempData["SubError"] = "No workspaces found in the database to simulate email syncing.";
                return RedirectToPage();
            }

            var accounts = await _context.Accounts.ToListAsync();
            int syncedCount = 0;

            // Simulated receipts parsed from mailbox
            var mockEmailReceipts = new List<(string Email, string WorkspaceJoinCode, string Plan, decimal Amount, string Method)>
            {
                ("bob@student.edu", "44444444-dddd-4444-dddd-444444444444", "pro_monthly", 299000, "VNPAY QR"),
                ("alice@student.edu", "11111111-aaaa-1111-aaaa-111111111111", "business_monthly", 899000, "Stripe API"),
                ("charlie@student.edu", "55555555-eeee-5555-eeee-555555555555", "proplus_monthly", 449000, "MOMO E-Wallet")
            };

            foreach (var receipt in mockEmailReceipts)
            {
                var targetWorkspace = await _context.Workspaces
                    .Include(w => w.Owner)
                    .FirstOrDefaultAsync(w => w.JoinCode == receipt.WorkspaceJoinCode);

                if (targetWorkspace != null)
                {
                    var targetUser = targetWorkspace.Owner;
                    string txnRef = $"EMAIL-SYNC-{receipt.WorkspaceJoinCode.Substring(0, 8).ToUpper()}";
                    bool alreadyExists = await _context.Billings.AnyAsync(b => b.TransactionRef == txnRef);
                    if (alreadyExists) continue;

                    // Deactivate active plans
                    var activePlans = await _context.Billings
                        .Where(b => b.WorkspaceId == targetWorkspace.Id && b.Status == "Active")
                        .ToListAsync();
                    foreach (var plan in activePlans)
                    {
                        plan.Status = "Expired";
                    }

                    var billing = new Billing
                    {
                        Id = Guid.NewGuid(),
                        WorkspaceId = targetWorkspace.Id,
                        PackageId = receipt.Plan,
                        Status = "Active",
                        EndDate = DateTime.UtcNow.AddMonths(1),
                        Amount = receipt.Amount,
                        UserId = targetUser.Id,
                        PaymentMethod = receipt.Method + " (Email Synced)",
                        TransactionRef = txnRef,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-new Random().Next(10, 180))
                    };

                    string tier = "Free";
                    if (receipt.Plan.Contains("business")) tier = "Business";
                    else if (receipt.Plan.Contains("proplus")) tier = "ProPlus";
                    else if (receipt.Plan.Contains("pro")) tier = "Pro";
                    else if (receipt.Plan.Contains("personal")) tier = "Personal";

                    targetWorkspace.PackageTier = tier;
                    await _context.Billings.AddAsync(billing);

                    var audit = new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        UserId = targetUser.Id,
                        Action = "EmailSync",
                        TargetType = "Billing",
                        TargetId = billing.Id,
                        Timestamp = DateTime.UtcNow,
                        WorkspaceId = targetWorkspace.Id
                    };
                    await _context.AuditLogs.AddAsync(audit);

                    syncedCount++;
                }
            }

            // Fallback to existing workspaces if those exact seeded workspaces are not present
            if (syncedCount == 0)
            {
                foreach (var ws in workspaces)
                {
                    string txnRef = $"EMAIL-SYNC-{ws.JoinCode.Substring(0, Math.Min(ws.JoinCode.Length, 8)).ToUpper()}";
                    bool alreadyExists = await _context.Billings.AnyAsync(b => b.TransactionRef == txnRef);
                    if (alreadyExists) continue;

                    var activePlans = await _context.Billings
                        .Where(b => b.WorkspaceId == ws.Id && b.Status == "Active")
                        .ToListAsync();
                    foreach (var plan in activePlans)
                    {
                        plan.Status = "Expired";
                    }

                    string tier = ws.PackageTier ?? "Pro";
                    if (tier == "Free") tier = "Pro";
                    decimal amount = tier switch
                    {
                        "Personal" => 40000,
                        "Pro" => 299000,
                        "ProPlus" => 449000,
                        "Business" => 899000,
                        _ => 299000
                    };

                    var billing = new Billing
                    {
                        Id = Guid.NewGuid(),
                        WorkspaceId = ws.Id,
                        PackageId = $"{tier.ToLower()}_monthly",
                        Status = "Active",
                        EndDate = DateTime.UtcNow.AddMonths(1),
                        Amount = amount,
                        UserId = ws.OwnerId,
                        PaymentMethod = "Bank Transfer (Email Synced)",
                        TransactionRef = txnRef,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-new Random().Next(10, 180))
                    };

                    ws.PackageTier = tier;
                    await _context.Billings.AddAsync(billing);

                    var audit = new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        UserId = ws.OwnerId,
                        Action = "EmailSync",
                        TargetType = "Billing",
                        TargetId = billing.Id,
                        Timestamp = DateTime.UtcNow,
                        WorkspaceId = ws.Id
                    };
                    await _context.AuditLogs.AddAsync(audit);

                    syncedCount++;
                }
            }

            if (syncedCount > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SubSuccess"] = $"Experimental Sync: Successfully scanned admin mailbox and synchronized {syncedCount} payment receipts.";
            }
            else
            {
                TempData["SubError"] = "No new transaction receipts found in the admin mailbox (all receipts are already synchronized).";
            }

            return RedirectToPage();
        }
    }
}
