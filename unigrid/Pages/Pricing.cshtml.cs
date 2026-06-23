using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace unigrid.Pages;

public class PricingModel : PageModel
{
    private readonly UniGridDbContext _context;
    private readonly IMemoryCache _cache;

    public PricingModel(UniGridDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    [BindProperty(SupportsGet = true)]
    public string? JoinCode { get; set; }

    public string? ActiveWorkspaceJoinCode { get; set; }
    public bool IsWorkspaceUpgrade { get; set; }

    public string CurrentPlan { get; set; } = "None";
    public AdminSettings Settings { get; set; } = null!;

    public int GetPlanRank(string? plan)
    {
        if (string.IsNullOrEmpty(plan) || plan == "Free") return 0;
        var settings = AdminSettings.Load(_context);
        var index = settings.Plans.FindIndex(p => p.Id.Equals(plan, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(plan, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index + 1 : 0;
    }

    public async System.Threading.Tasks.Task OnGetAsync()
    {
        Settings = AdminSettings.Load(_context);
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (!string.IsNullOrEmpty(accountIdClaim))
        {
            var accountId = Guid.Parse(accountIdClaim);
            var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
            if (userProfile != null)
            {
                CurrentPlan = userProfile.SubscriptionTier ?? "Free";
                ViewData["UserName"] = userProfile.FullName;
                ViewData["UserInitials"] = string.Concat(userProfile.FullName.Split(' ').Select(n => n[0]));

                // Fetch Workspaces for sidebar
                var userWorkspaces = await _context.Workspaces
                    .Where(w => !w.IsDisabled && (w.OwnerId == userProfile.Id || w.WorkspaceMembers.Any(m => !m.IsDisabled && m.UserId == userProfile.Id)))
                    .ToListAsync();
                ViewData["Workspaces"] = userWorkspaces;

                // If workspace context is provided, align pricing page with workspace's active package tier
                if (!string.IsNullOrEmpty(JoinCode))
                {
                    var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.JoinCode == JoinCode);
                    if (workspace != null)
                    {
                        CurrentPlan = workspace.PackageTier ?? "Free";
                        ActiveWorkspaceJoinCode = JoinCode;
                        IsWorkspaceUpgrade = true;
                    }
                }
            }
        }
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostUpgradeAsync(string tier, string billingPeriod)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return RedirectToPage("/Login");

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile != null)
        {
            // Determine billing period (default to monthly if empty/null)
            billingPeriod = (billingPeriod ?? "monthly").ToLower();
            
            // Calculate plan cost dynamically from admin settings
            var settings = AdminSettings.Load(_context);
            decimal amount = 0;
            var plan = settings.Plans.FirstOrDefault(p => p.Id.Equals(tier, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(tier, StringComparison.OrdinalIgnoreCase));
            if (plan != null)
            {
                amount = billingPeriod == "yearly" ? plan.YearlyPrice : plan.MonthlyPrice;
            }
            else
            {
                TempData["UpgradeError"] = $"Plan tier '{tier}' is not recognized.";
                return RedirectToPage("/Pricing", new { joinCode = JoinCode });
            }

            Workspace? workspace = null;

            // If upgrading an active workspace, update workspace's package tier directly
            if (!string.IsNullOrEmpty(JoinCode))
            {
                workspace = await _context.Workspaces.Include(w => w.Owner).FirstOrDefaultAsync(w => w.JoinCode == JoinCode);
                if (workspace != null)
                {
                    if (plan.MemberLimit > 0)
                    {
                        int memberCount = await _context.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == workspace.Id);
                        if (memberCount > plan.MemberLimit)
                        {
                            TempData["UpgradeError"] = $"Cannot switch this Workspace to the {plan.Name} plan because it currently has more than {plan.MemberLimit} members. The {plan.Name} plan allows a maximum of {plan.MemberLimit} members.";
                            return RedirectToPage("/Pricing", new { joinCode = JoinCode });
                        }
                    }
                    workspace.PackageTier = tier;

                    if (workspace.WorkspaceType == "Personal" || tier == "Personal")
                    {
                        userProfile.SubscriptionTier = tier;
                        userProfile.SubscriptionExpires = billingPeriod == "yearly" ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1);

                        workspace.Owner.SubscriptionTier = tier;
                        workspace.Owner.SubscriptionExpires = userProfile.SubscriptionExpires;
                    }
                }
            }

            // If no workspace context, check or create a default personal workspace
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
                        PackageTier = tier,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _context.Workspaces.AddAsync(workspace);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    workspace.PackageTier = tier;
                }

                // Fallback to personal subscription upgrade
                userProfile.SubscriptionTier = tier;
                userProfile.SubscriptionExpires = billingPeriod == "yearly" ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1);
            }

            // Terminate existing active billings for this workspace
            var activeBillings = await _context.Billings
                .Where(b => b.WorkspaceId == workspace.Id && b.Status == "Active")
                .ToListAsync();
            foreach (var active in activeBillings)
            {
                active.Status = "Expired";
            }

            // Create a detailed billing transaction record
            var billing = new Billing
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                PackageId = $"{tier.ToLower()}_{billingPeriod}",
                Status = "Active",
                EndDate = billingPeriod == "yearly" ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1),
                Amount = amount,
                UserId = userProfile.Id,
                PaymentMethod = "Credit Card (Simulated Checkout)",
                TransactionRef = "TXN-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                CreatedAt = DateTime.UtcNow
            };

            await _context.Billings.AddAsync(billing);

            // Add an audit log entry for the transaction
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

            // Evict cache
            if (workspace != null)
            {
                _cache.Remove($"Workspace_{workspace.JoinCode}");
                _cache.Remove($"WorkspaceMembers_{workspace.Id}");
            }
            _cache.Remove($"UserWorkspaces_{userProfile.Id}");
            _cache.Remove($"User_{accountId}");

            if (!string.IsNullOrEmpty(JoinCode))
            {
                return RedirectToPage("/WorkspaceDetail", new { joinCode = JoinCode });
            }
        }

        return RedirectToPage();
    }

    public async System.Threading.Tasks.Task<IActionResult> OnPostSubmitFederationRequestAsync(string businessName, string contactPhone)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (string.IsNullOrEmpty(accountIdClaim)) return new BadRequestResult();

        var accountId = Guid.Parse(accountIdClaim);
        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile != null)
        {
            var adminNotification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userProfile.Id,
                Message = $"The Enterprise Federation registration request for '{businessName}' (Phone: {contactPhone}) has been successfully submitted and is awaiting administrator review.",
                Type = "FederationRequest",
                Link = "/workspaces",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            
            await _context.Notifications.AddAsync(adminNotification);
            await _context.SaveChangesAsync();
        }

        return new JsonResult(new { success = true });
    }
}

public static class PlanColorHelper
{
    public static string GetBorderClass(string? color) => (color ?? "indigo").ToLower() switch
    {
        "teal" => "border-teal-400",
        "indigo" => "border-indigo-400",
        "violet" => "border-violet-500",
        "emerald" => "border-emerald-400",
        "slate" => "border-slate-800",
        _ => "border-indigo-400"
    };

    public static string GetTextClass(string? color) => (color ?? "indigo").ToLower() switch
    {
        "teal" => "text-teal-600",
        "indigo" => "text-indigo-600",
        "violet" => "text-violet-600",
        "emerald" => "text-emerald-600",
        "slate" => "text-slate-800",
        _ => "text-indigo-600"
    };

    public static string GetBgClass(string? color) => (color ?? "indigo").ToLower() switch
    {
        "teal" => "bg-teal-600 hover:bg-teal-700",
        "indigo" => "bg-indigo-600 hover:bg-indigo-700",
        "violet" => "bg-violet-600 hover:bg-violet-700",
        "emerald" => "bg-emerald-600 hover:bg-emerald-700",
        "slate" => "bg-slate-800 hover:bg-slate-900",
        _ => "bg-indigo-600 hover:bg-indigo-700"
    };

    public static string GetShadowClass(string? color) => (color ?? "indigo").ToLower() switch
    {
        "teal" => "shadow-teal-100",
        "indigo" => "shadow-indigo-100",
        "violet" => "shadow-violet-100",
        "emerald" => "shadow-emerald-100",
        "slate" => "shadow-slate-200",
        _ => "shadow-indigo-100"
    };
    
    public static string GetRingClass(string? color) => (color ?? "indigo").ToLower() switch
    {
        "teal" => "ring-teal-500/20",
        "indigo" => "ring-indigo-500/20",
        "violet" => "ring-violet-500/20",
        "emerald" => "ring-emerald-500/20",
        "slate" => "ring-slate-800/20",
        _ => "ring-indigo-500/20"
    };
}
