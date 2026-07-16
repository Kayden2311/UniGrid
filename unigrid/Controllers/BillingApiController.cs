using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PayOS;
using PayOS.Models.V2.PaymentRequests;
using PayOS.Models.Webhooks;
using unigrid.Data;
using unigrid.Models;
using unigrid.Services;

namespace unigrid.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingApiController : ControllerBase
{
    private readonly UniGridDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly PayOSClient _payOS;
    private readonly IConfiguration _configuration;
    private readonly INotificationService _notificationService;
    private readonly ILogger<BillingApiController> _logger;

    public BillingApiController(
        UniGridDbContext context,
        IMemoryCache cache,
        PayOSClient payOS,
        IConfiguration configuration,
        INotificationService notificationService,
        ILogger<BillingApiController> logger)
    {
        _context = context;
        _cache = cache;
        _payOS = payOS;
        _configuration = configuration;
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpPost("create-checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request)
    {
        if (!IsPayOSConfigured())
        {
            return StatusCode(503, new { message = "payOS is not configured on this server." });
        }

        if (string.IsNullOrWhiteSpace(request.Tier))
        {
            return BadRequest(new { message = "Plan tier is required." });
        }

        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (!Guid.TryParse(accountIdClaim, out var accountId))
        {
            return Unauthorized(new { message = "Authentication context missing." });
        }

        var userProfile = await _context.Users.FirstOrDefaultAsync(u => u.AccountId == accountId);
        if (userProfile == null)
        {
            return Unauthorized(new { message = "User profile not found." });
        }

        var settings = AdminSettings.Load(_context);
        var billingPeriod = (request.BillingPeriod ?? "monthly").ToLowerInvariant();
        if (billingPeriod is not ("monthly" or "yearly"))
        {
            return BadRequest(new { message = "Billing period must be monthly or yearly." });
        }

        var plan = settings.Plans.FirstOrDefault(p =>
            p.Id.Equals(request.Tier, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(request.Tier, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            return BadRequest(new { message = $"Plan tier '{request.Tier}' is not recognized." });
        }

        var decimalAmount = billingPeriod == "yearly" ? plan.YearlyPrice : plan.MonthlyPrice;
        if (decimalAmount <= 0 || decimalAmount != decimal.Truncate(decimalAmount) || decimalAmount > int.MaxValue)
        {
            return BadRequest(new { message = "The selected plan does not have a valid VND price." });
        }
        var amount = decimal.ToInt32(decimalAmount);

        var workspace = await ResolveWorkspaceAsync(request.JoinCode, userProfile, plan.MemberLimit);
        if (workspace == null)
        {
            return StatusCode(403, new { message = "You do not have permission to upgrade this workspace." });
        }

        var orderCode = await GenerateOrderCodeAsync();
        var transactionRef = $"PAYOS-{orderCode}";
        var billing = new Billing
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            PackageId = $"{plan.Id.ToLowerInvariant()}_{billingPeriod}",
            Status = "Pending",
            EndDate = billingPeriod == "yearly" ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1),
            Amount = amount,
            UserId = userProfile.Id,
            PaymentMethod = "payOS",
            TransactionRef = transactionRef,
            CreatedAt = DateTime.UtcNow
        };

        _context.Billings.Add(billing);
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userProfile.Id,
            Action = "PayOSCheckoutCreated",
            TargetType = "Billing",
            TargetId = billing.Id,
            Timestamp = DateTime.UtcNow,
            WorkspaceId = workspace.Id
        });
        await _context.SaveChangesAsync();

        var returnUrl = BuildCallbackUrl("ReturnUrl", "/Pricing?payment=returned");
        var cancelUrl = BuildCallbackUrl("CancelUrl", "/Pricing?payment=cancelled");
        var description = $"UG{orderCode % 10_000_000:D7}";

        try
        {
            var paymentLink = await _payOS.PaymentRequests.CreateAsync(new CreatePaymentLinkRequest
            {
                OrderCode = orderCode,
                Amount = amount,
                Description = description,
                BuyerName = userProfile.FullName,
                BuyerEmail = User.Identity?.Name,
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl,
                ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()
            });

            return Ok(new
            {
                success = true,
                amount,
                checkoutUrl = paymentLink.CheckoutUrl,
                transactionRef,
                orderCode,
                tier = plan.Id,
                billingPeriod,
                joinCode = workspace.JoinCode
            });
        }
        catch (Exception ex)
        {
            billing.Status = "Failed";
            await _context.SaveChangesAsync();
            _logger.LogError(ex, "payOS failed to create payment link for {TransactionRef}", transactionRef);
            return StatusCode(502, new { message = "Could not create the payOS payment link. Please try again." });
        }
    }

    [HttpPost("payos-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PayOSWebhook([FromBody] Webhook webhook)
    {
        if (!IsPayOSConfigured())
        {
            return StatusCode(503);
        }

        WebhookData verifiedData;
        try
        {
            verifiedData = await _payOS.Webhooks.VerifyAsync(webhook);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected a payOS webhook with an invalid signature.");
            return BadRequest(new { message = "Invalid webhook signature." });
        }

        // payOS sends sample data while confirming a webhook URL. A missing order is
        // acknowledged so the URL can be registered without mutating application data.
        var transactionRef = $"PAYOS-{verifiedData.OrderCode}";
        var billing = await _context.Billings
            .Include(b => b.Workspace)
            .ThenInclude(w => w.Owner)
            .FirstOrDefaultAsync(b => b.TransactionRef == transactionRef);
        if (billing == null)
        {
            _logger.LogInformation("Acknowledged payOS webhook for unknown/sample order {OrderCode}.", verifiedData.OrderCode);
            return Ok(new { success = true });
        }

        if (billing.Status == "Active")
        {
            return Ok(new { success = true });
        }

        if (verifiedData.Code != "00" || verifiedData.Amount != decimal.ToInt64(billing.Amount ?? 0))
        {
            _logger.LogWarning(
                "payOS webhook did not match billing {BillingId}. Code={Code}, Paid={Paid}, Expected={Expected}",
                billing.Id, verifiedData.Code, verifiedData.Amount, billing.Amount);
            return BadRequest(new { message = "Payment data does not match the billing record." });
        }

        await ActivateBillingAsync(billing);
        return Ok(new { success = true });
    }

    [HttpGet("payment-status/{orderCode:long}")]
    public async Task<IActionResult> GetPaymentStatus(long orderCode)
    {
        var accountIdClaim = User.FindFirst("AccountId")?.Value;
        if (!Guid.TryParse(accountIdClaim, out var accountId)) return Unauthorized();

        var userId = await _context.Users
            .Where(u => u.AccountId == accountId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();
        if (userId == null) return Unauthorized();

        var billing = await _context.Billings
            .Include(b => b.Workspace)
            .FirstOrDefaultAsync(b => b.TransactionRef == $"PAYOS-{orderCode}" && b.UserId == userId);
        if (billing == null) return NotFound();

        return Ok(new { success = true, status = billing.Status, joinCode = billing.Workspace.JoinCode });
    }

    private async Task<Workspace?> ResolveWorkspaceAsync(string? joinCode, User userProfile, int memberLimit)
    {
        if (!string.IsNullOrWhiteSpace(joinCode))
        {
            var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.JoinCode == joinCode);
            if (workspace == null) return null;

            var canAccess = workspace.OwnerId == userProfile.Id || await _context.WorkspaceMembers.AnyAsync(wm =>
                !wm.IsDisabled && wm.WorkspaceId == workspace.Id && wm.UserId == userProfile.Id);
            if (!canAccess) return null;

            if (memberLimit > 0)
            {
                var memberCount = await _context.WorkspaceMembers.CountAsync(wm =>
                    !wm.IsDisabled && wm.WorkspaceId == workspace.Id);
                if (memberCount > memberLimit) return null;
            }
            return workspace;
        }

        var personalWorkspace = await _context.Workspaces.FirstOrDefaultAsync(w =>
            w.OwnerId == userProfile.Id && w.WorkspaceType == "Personal");
        if (personalWorkspace != null) return personalWorkspace;

        personalWorkspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = $"{userProfile.FullName}'s Personal Workspace",
            JoinCode = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            OwnerId = userProfile.Id,
            WorkspaceType = "Personal",
            PackageTier = "Free",
            CreatedAt = DateTime.UtcNow
        };
        _context.Workspaces.Add(personalWorkspace);
        return personalWorkspace;
    }

    private async Task<long> GenerateOrderCodeAsync()
    {
        var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        while (await _context.Billings.AnyAsync(b => b.TransactionRef == $"PAYOS-{orderCode}"))
        {
            orderCode++;
        }
        return orderCode;
    }

    private async System.Threading.Tasks.Task ActivateBillingAsync(Billing billing)
    {
        var workspace = billing.Workspace;
        var packageId = billing.PackageId.ToLowerInvariant();
        var tier = packageId.Contains("business") ? "Business"
            : packageId.Contains("proplus") ? "ProPlus"
            : packageId.Contains("pro") ? "Pro"
            : packageId.Contains("personal") ? "Personal"
            : "Free";
        var duration = packageId.Contains("yearly") ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1);

        var otherActiveBillings = await _context.Billings
            .Where(b => b.WorkspaceId == workspace.Id && b.Id != billing.Id && b.Status == "Active")
            .ToListAsync();
        foreach (var other in otherActiveBillings) other.Status = "Expired";

        billing.Status = "Active";
        billing.EndDate = duration;
        workspace.PackageTier = tier;
        if (tier is "Pro" or "ProPlus" or "Business") workspace.WorkspaceType = "Group";
        if (workspace.Owner != null && (workspace.WorkspaceType == "Personal" || tier == "Personal"))
        {
            workspace.Owner.SubscriptionTier = tier;
            workspace.Owner.SubscriptionExpires = duration;
        }

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = billing.UserId ?? workspace.OwnerId,
            Action = "PayOSPaymentConfirmed",
            TargetType = "Billing",
            TargetId = billing.Id,
            Timestamp = DateTime.UtcNow,
            WorkspaceId = workspace.Id
        });
        await _context.SaveChangesAsync();

        _cache.Remove($"Workspace_{workspace.JoinCode}");
        _cache.Remove($"WorkspaceMembers_{workspace.Id}");
        if (billing.UserId.HasValue) _cache.Remove($"UserWorkspaces_{billing.UserId.Value}");

        await _notificationService.CreateAndSendNotificationAsync(
            workspace.OwnerId,
            $"Payment received. Your {tier} plan is now active for workspace '{workspace.Name}'.",
            "SubscriptionNotification",
            "/Pricing",
            billing.Id);
    }

    private bool IsPayOSConfigured() =>
        !string.IsNullOrWhiteSpace(_configuration["PayOS:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_configuration["PayOS:ApiKey"]) &&
        !string.IsNullOrWhiteSpace(_configuration["PayOS:ChecksumKey"]);

    private string BuildCallbackUrl(string settingName, string fallbackPath)
    {
        var configured = _configuration[$"PayOS:{settingName}"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{fallbackPath}";
    }
}

public class CheckoutRequest
{
    public string Tier { get; set; } = null!;
    public string? BillingPeriod { get; set; }
    public string? JoinCode { get; set; }
}
