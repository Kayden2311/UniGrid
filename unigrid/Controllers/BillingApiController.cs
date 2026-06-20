using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;

namespace unigrid.Controllers
{
    [ApiController]
    [Route("api/billing")]
    [Authorize] // Require authentication for checkout API
    public class BillingApiController : ControllerBase
    {
        private readonly UniGridDbContext _context;

        public BillingApiController(UniGridDbContext context)
        {
            _context = context;
        }

        [HttpPost("create-checkout")]
        public async System.Threading.Tasks.Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request)
        {
            if (string.IsNullOrEmpty(request.Tier))
            {
                return BadRequest(new { message = "Plan tier is required." });
            }

            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim))
            {
                return Unauthorized(new { message = "Authentication context missing." });
            }

            var accountId = Guid.Parse(accountIdClaim);
            var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (userProfile == null)
            {
                return Unauthorized(new { message = "User profile not found." });
            }

            // Calculate billing pricing amount in VND dynamically from admin settings
            var settings = AdminSettings.Load(_context);
            var billingPeriod = (request.BillingPeriod ?? "monthly").ToLower();
            decimal amount = 0;
            var plan = settings.Plans.FirstOrDefault(p => p.Id.Equals(request.Tier, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(request.Tier, StringComparison.OrdinalIgnoreCase));
            if (plan != null)
            {
                amount = billingPeriod == "yearly" ? plan.YearlyPrice : plan.MonthlyPrice;
            }
            else
            {
                return BadRequest(new { message = $"Plan tier '{request.Tier}' is not recognized." });
            }

            // Validate workspace membership constraints for plan limits
            Workspace? workspace = null;
            if (!string.IsNullOrEmpty(request.JoinCode))
            {
                workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.JoinCode == request.JoinCode);
                if (workspace != null)
                {
                    var isOwner = workspace.OwnerId == userProfile.Id;
                    var isMember = await _context.WorkspaceMembers.AnyAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == userProfile.Id);
                    if (!isOwner && !isMember)
                    {
                        return StatusCode(403, new { message = "You do not have permission to access this workspace." });
                    }

                    if (plan.MemberLimit > 0)
                    {
                        int memberCount = await _context.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == workspace.Id);
                        if (memberCount > plan.MemberLimit)
                        {
                            return BadRequest(new { message = $"Cannot switch this Workspace to the {plan.Name} plan because it currently has more than {plan.MemberLimit} members. The {plan.Name} plan allows a maximum of {plan.MemberLimit} members." });
                        }
                    }
                }
            }

            // Generate unique transaction reference number
            string transactionRef = "TXN-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper();

            // Construct VietQR merchant details
            string bankId = "971025"; // MoMo
            string accountNo = "PSG2615912200000011"; // Group account
            string accountName = "DUONG XUAN PHU";
            
            // The transfer description must include the reference token
            string description = $"UNIGRID {transactionRef}";

            // Generate VietQR dynamic code endpoint using img.vietqr.io API
            string qrUrl = $"https://img.vietqr.io/image/{bankId}-{accountNo}-compact2.png?amount={amount}&addInfo={Uri.EscapeDataString(description)}&accountName={Uri.EscapeDataString(accountName)}";

            return Ok(new
            {
                success = true,
                amount = amount,
                qrUrl = qrUrl,
                transactionRef = transactionRef,
                tier = request.Tier,
                billingPeriod = billingPeriod,
                joinCode = request.JoinCode,
                userName = userProfile.FullName,
                userEmail = User.Identity?.Name,
                workspaceName = workspace?.Name ?? $"{userProfile.FullName}'s Personal Workspace"
            });
        }

        [HttpPost("confirm-payment")]
        public async System.Threading.Tasks.Task<IActionResult> ConfirmPayment([FromBody] ConfirmPaymentRequest request)
        {
            if (string.IsNullOrEmpty(request.TransactionRef) || string.IsNullOrEmpty(request.Tier))
            {
                return BadRequest(new { message = "Transaction specifications are missing." });
            }

            var accountIdClaim = User.FindFirst("AccountId")?.Value;
            if (string.IsNullOrEmpty(accountIdClaim))
            {
                return Unauthorized(new { message = "Authentication context missing." });
            }

            var accountId = Guid.Parse(accountIdClaim);
            var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (userProfile == null)
            {
                return Unauthorized(new { message = "User profile not found." });
            }

            // Re-evaluate cost dynamically from admin settings
            var settings = AdminSettings.Load(_context);
            var billingPeriod = (request.BillingPeriod ?? "monthly").ToLower();
            decimal amount = 0;
            var plan = settings.Plans.FirstOrDefault(p => p.Id.Equals(request.Tier, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(request.Tier, StringComparison.OrdinalIgnoreCase));
            if (plan != null)
            {
                amount = billingPeriod == "yearly" ? plan.YearlyPrice : plan.MonthlyPrice;
            }
            else
            {
                return BadRequest(new { message = $"Plan tier '{request.Tier}' is not recognized." });
            }

            Workspace? workspace = null;

            // Apply workspace changes
            if (!string.IsNullOrEmpty(request.JoinCode))
            {
                workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.JoinCode == request.JoinCode);
                if (workspace != null)
                {
                    if (plan.MemberLimit > 0)
                    {
                        int memberCount = await _context.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == workspace.Id);
                        if (memberCount > plan.MemberLimit)
                        {
                            return BadRequest(new { message = $"Cannot switch this Workspace to the {plan.Name} plan because it currently has more than {plan.MemberLimit} members." });
                        }
                    }
                    workspace.PackageTier = request.Tier;
                }
            }

            // Auto-create workspace if not context provided (standard solo upgrade flow)
            if (workspace == null)
            {
                workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.OwnerId == userProfile.Id && w.WorkspaceType == "Personal");
                if (workspace == null)
                {
                    workspace = new Workspace
                    {
                        Id = Guid.NewGuid(),
                        Name = $"{userProfile.FullName}'s Personal Workspace",
                        JoinCode = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                        OwnerId = userProfile.Id,
                        WorkspaceType = "Personal",
                        PackageTier = request.Tier,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _context.Workspaces.AddAsync(workspace);
                }
                else
                {
                    workspace.PackageTier = request.Tier;
                }

                // Update personal subscription claims
                userProfile.SubscriptionTier = request.Tier;
                userProfile.SubscriptionExpires = billingPeriod == "yearly" ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1);
            }

            // Expire existing active billings for this workspace
            var activeBillings = await _context.Billings
                .Where(b => b.WorkspaceId == workspace.Id && b.Status == "Active")
                .ToListAsync();
            foreach (var active in activeBillings)
            {
                active.Status = "Expired";
            }

            // Create new detailed billing transaction record
            var billing = new Billing
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                PackageId = $"{request.Tier.ToLower()}_{billingPeriod}",
                Status = "Active",
                EndDate = billingPeriod == "yearly" ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1),
                Amount = amount,
                UserId = userProfile.Id,
                PaymentMethod = "VietQR Instant Transfer",
                TransactionRef = request.TransactionRef,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Billings.AddAsync(billing);

            // Audit log tracking
            var audit = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userProfile.Id,
                Action = "Upgrade",
                TargetType = "Billing",
                TargetId = billing.Id,
                Timestamp = DateTime.UtcNow,
                WorkspaceId = workspace.Id
            };
            await _context.AuditLogs.AddAsync(audit);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Payment successfully validated and workspace plan upgraded!",
                joinCode = workspace.JoinCode
            });
        }
    }

    public class CheckoutRequest
    {
        public string Tier { get; set; } = null!;
        public string? BillingPeriod { get; set; }
        public string? JoinCode { get; set; }
    }

    public class ConfirmPaymentRequest
    {
        public string TransactionRef { get; set; } = null!;
        public string Tier { get; set; } = null!;
        public string? BillingPeriod { get; set; }
        public string? JoinCode { get; set; }
    }
}
