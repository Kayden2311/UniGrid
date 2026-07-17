using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using unigrid.Data;
using unigrid.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace unigrid.Pages.Admin
{
    [Authorize(Roles = "1,3")]
    public class IndexModel : PageModel
    {
        private readonly UniGridDbContext _context;

        public IndexModel(UniGridDbContext context)
        {
            _context = context;
        }

        // Platform Summary
        public int TotalAccounts { get; set; }
        public int LockedAccounts { get; set; }
        public int TotalUsers { get; set; }
        public int TotalModerators { get; set; }
        public int TotalWorkspaces { get; set; }
        public int PersonalWorkspaces { get; set; }
        public int GroupWorkspaces { get; set; }
        public int TotalFederations { get; set; }
        public int TotalFilesCount { get; set; }
        public int TotalTasksCount { get; set; }

        // Anonymous website traffic aggregates
        public long TrafficToday { get; set; }
        public long TotalTraffic { get; set; }
        public string TrafficChartLabelsJson { get; set; } = "[]";
        public string TrafficChartDataJson { get; set; } = "[]";
        public List<TopTrafficPageItem> TopTrafficPages { get; set; } = new();

        public class TopTrafficPageItem
        {
            public string Path { get; set; } = "/";
            public long VisitCount { get; set; }
        }

        // Income Metrics
        public long ProjectedMonthlyRevenue { get; set; }
        public int ActiveSubscriptionsCount { get; set; }
        public double AverageRevenuePerSubscription { get; set; }

        // Subscriptions Breakdown
        public int BusinessSubscribers { get; set; }
        public int ProPlusSubscribers { get; set; }
        public int ProSubscribers { get; set; }
        public int PersonalSubscribers { get; set; }
        public int FreeTierWorkspaces { get; set; }

        // Activity Feed
        public List<AuditLog> RecentAuditLogs { get; set; } = new();

        // Operational and Pricing settings in JSON formats
        [BindProperty]
        public string OperationCostsJson { get; set; } = "[]";

        [BindProperty]
        public string PlansJson { get; set; } = "[]";

        [BindProperty]
        public string CostMonth { get; set; } = string.Empty;

        public class PlanBreakdownItem
        {
            public string PlanId { get; set; } = string.Empty;
            public string PlanName { get; set; } = string.Empty;
            public decimal MonthlyPrice { get; set; }
            public string ColorClass { get; set; } = "slate";
            public int Count { get; set; }
        }
        public List<PlanBreakdownItem> PlanBreakdown { get; set; } = new();

        public decimal TotalCosts { get; set; }
        public decimal ProjectedNetProfit { get; set; }

        // Chart Data (past 6 months)
        public string ChartLabelsJson { get; set; } = "[]";
        public string RevenueChartDataJson { get; set; } = "[]";
        public string CostChartDataJson { get; set; } = "[]";
        public string ProfitChartDataJson { get; set; } = "[]";

        [BindProperty(SupportsGet = true)]
        public string? SelectedMonth { get; set; }

        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> AvailableMonths { get; set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public async System.Threading.Tasks.Task<IActionResult> OnGetAsync()
        {
            // Load settings and json configurations
            var settings = unigrid.Models.AdminSettings.Load(_context);
            PlansJson = JsonSerializer.Serialize(settings.Plans);

            // Populate AvailableMonths dropdown (last 6 months)
            var now = DateTime.UtcNow;
            AvailableMonths.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = "current",
                Text = "Current Projection (Live)",
                Selected = (string.IsNullOrEmpty(SelectedMonth) || SelectedMonth == "current")
            });
            for (int i = 0; i < 6; i++)
            {
                var monthDate = now.AddMonths(-i);
                var value = monthDate.ToString("yyyy-MM");
                var text = monthDate.ToString("MMMM yyyy");
                AvailableMonths.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = value,
                    Text = text,
                    Selected = (SelectedMonth == value)
                });
            }

            var selectedCostMonth = ResolveMonth(SelectedMonth, now);
            CostMonth = selectedCostMonth.ToString("yyyy-MM");
            var savedMonthlyCosts = await _context.MonthlyProjectCosts
                .AsNoTracking()
                .Where(c => c.CostMonth == selectedCostMonth)
                .OrderBy(c => c.Id)
                .ToListAsync();

            // Use the existing configured costs as the editable starting point for
            // the current month only. Historical months start empty until entered.
            var displayedCosts = savedMonthlyCosts.Count > 0
                ? savedMonthlyCosts.Select(c => new OperationCostSetting
                {
                    Name = c.Name,
                    Amount = c.Amount,
                    IsDisabled = c.IsDisabled
                }).ToList()
                : selectedCostMonth == new DateOnly(now.Year, now.Month, 1)
                    ? settings.OperationCosts
                    : new List<OperationCostSetting>();
            OperationCostsJson = JsonSerializer.Serialize(displayedCosts);
            TotalCosts = displayedCosts.Where(c => !c.IsDisabled).Sum(c => c.Amount);

            // Fetch accounts and users
            TotalAccounts = await _context.Accounts.CountAsync();
            LockedAccounts = await _context.Accounts.CountAsync(a => a.IsLocked == true);
            TotalUsers = await _context.Users.CountAsync();
            TotalModerators = await _context.Moderators.CountAsync();

            // Workspaces stats
            TotalWorkspaces = await _context.Workspaces.CountAsync();
            PersonalWorkspaces = await _context.Workspaces.CountAsync(w => w.WorkspaceType == "Personal");
            GroupWorkspaces = await _context.Workspaces.CountAsync(w => w.WorkspaceType == "Group");

            // Federations, tasks, files
            TotalFederations = await _context.WorkspaceFederations.CountAsync();
            TotalFilesCount = await _context.WorkspaceFiles.CountAsync();
            TotalTasksCount = await _context.Tasks.CountAsync();

            // Fetch all billings for revenue & charting calculations
            var allBillings = await _context.Billings
                .Include(b => b.Workspace)
                .ToListAsync();

            // Fetch all workspaces with package tier set directly (not free) once to reuse in both scopes and charting loop
            var workspacesWithTierDirect = await _context.Workspaces
                .Where(w => !string.IsNullOrEmpty(w.PackageTier) && w.PackageTier != "Free")
                .ToListAsync();

            // Count subscribers dynamically for each plan
            PlanBreakdown = settings.Plans.Select(p => new PlanBreakdownItem
            {
                PlanId = p.Id,
                PlanName = p.Name,
                MonthlyPrice = p.MonthlyPrice,
                ColorClass = p.ColorClass,
                Count = 0
            }).ToList();

            var freeTierItem = new PlanBreakdownItem
            {
                PlanId = "Free",
                PlanName = "Free",
                MonthlyPrice = 0,
                ColorClass = "slate",
                Count = 0
            };
            PlanBreakdown.Add(freeTierItem);

            long revenue = 0;
            int activeSubsCount = 0;

            bool isSpecificMonth = !string.IsNullOrEmpty(SelectedMonth) && SelectedMonth != "current";
            if (isSpecificMonth)
            {
                // Parse specific month
                var parts = SelectedMonth!.Split('-');
                int targetYear = int.Parse(parts[0]);
                int targetMonth = int.Parse(parts[1]);

                var firstDayOfMonth = new DateTime(targetYear, targetMonth, 1, 0, 0, 0, DateTimeKind.Utc);
                var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddSeconds(-1);

                // Filter billings active during that specific month
                var monthlyBillings = allBillings.Where(b => {
                    var billingCreated = b.CreatedAt ?? b.EndDate.AddYears(-1);
                    return billingCreated <= lastDayOfMonth && b.EndDate >= firstDayOfMonth;
                }).ToList();

                activeSubsCount = monthlyBillings.Count;

                foreach (var billing in monthlyBillings)
                {
                    var packageId = billing.PackageId.ToLower();
                    decimal itemAmount = billing.Amount ?? 0;
                    if (itemAmount == 0)
                    {
                        var pParts = packageId.Split('_');
                        if (pParts.Length > 0)
                        {
                            var planId = pParts[0];
                            var isYearly = packageId.Contains("yearly");
                            var plan = settings.Plans.FirstOrDefault(p => string.Equals(p.Id, planId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, planId, StringComparison.OrdinalIgnoreCase));
                            if (plan != null)
                            {
                                itemAmount = isYearly ? plan.YearlyPrice / 12 : plan.MonthlyPrice;
                            }
                        }
                    }
                    else
                    {
                        if (packageId.Contains("yearly"))
                        {
                            itemAmount = itemAmount / 12;
                        }
                    }

                    revenue += (long)itemAmount;

                    var parts2 = packageId.Split('_');
                    if (parts2.Length > 0)
                    {
                        var planId = parts2[0];
                        var match = PlanBreakdown.FirstOrDefault(b => 
                            string.Equals(b.PlanId, planId, StringComparison.OrdinalIgnoreCase) || 
                            string.Equals(b.PlanName, planId, StringComparison.OrdinalIgnoreCase));
                        if (match != null) match.Count++;
                        else freeTierItem.Count++;
                    }
                }

                // Workspaces set directly (created on or before last day of month)
                var monthlyWorkspaces = workspacesWithTierDirect
                    .Where(w => w.CreatedAt <= lastDayOfMonth)
                    .ToList();

                foreach (var ws in monthlyWorkspaces)
                {
                    if (!monthlyBillings.Any(b => b.WorkspaceId == ws.Id))
                    {
                        var tier = ws.PackageTier.ToLower();
                        var plan = settings.Plans.FirstOrDefault(p => string.Equals(p.Id, tier, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, tier, StringComparison.OrdinalIgnoreCase));
                        if (plan != null)
                        {
                            revenue += (long)plan.MonthlyPrice;
                            var match = PlanBreakdown.FirstOrDefault(b => 
                                string.Equals(b.PlanId, plan.Id, StringComparison.OrdinalIgnoreCase) || 
                                string.Equals(b.PlanName, plan.Name, StringComparison.OrdinalIgnoreCase));
                            if (match != null) match.Count++;
                        }
                        else
                        {
                            freeTierItem.Count++;
                        }
                        activeSubsCount++;
                    }
                }

                freeTierItem.Count = await _context.Workspaces.CountAsync(w => (string.IsNullOrEmpty(w.PackageTier) || w.PackageTier == "Free") && w.CreatedAt <= lastDayOfMonth);
                TotalWorkspaces = await _context.Workspaces.CountAsync(w => w.CreatedAt <= lastDayOfMonth);
            }
            else
            {
                // Current live snapshot projection
                var activeBillings = allBillings.Where(b => b.Status == "Active").ToList();
                activeSubsCount = activeBillings.Count;

                foreach (var billing in activeBillings)
                {
                    var packageId = billing.PackageId.ToLower();
                    decimal itemAmount = billing.Amount ?? 0;
                    if (itemAmount == 0)
                    {
                        var pParts = packageId.Split('_');
                        if (pParts.Length > 0)
                        {
                            var planId = pParts[0];
                            var isYearly = packageId.Contains("yearly");
                            var plan = settings.Plans.FirstOrDefault(p => string.Equals(p.Id, planId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, planId, StringComparison.OrdinalIgnoreCase));
                            if (plan != null)
                            {
                                itemAmount = isYearly ? plan.YearlyPrice / 12 : plan.MonthlyPrice;
                            }
                        }
                    }
                    else
                    {
                        if (packageId.Contains("yearly"))
                        {
                            itemAmount = itemAmount / 12;
                        }
                    }

                    revenue += (long)itemAmount;

                    var parts2 = packageId.Split('_');
                    if (parts2.Length > 0)
                    {
                        var planId = parts2[0];
                        var match = PlanBreakdown.FirstOrDefault(b => 
                            string.Equals(b.PlanId, planId, StringComparison.OrdinalIgnoreCase) || 
                            string.Equals(b.PlanName, planId, StringComparison.OrdinalIgnoreCase));
                        if (match != null) match.Count++;
                        else freeTierItem.Count++;
                    }
                }

                foreach (var ws in workspacesWithTierDirect)
                {
                    if (!activeBillings.Any(b => b.WorkspaceId == ws.Id))
                    {
                        var tier = ws.PackageTier.ToLower();
                        var plan = settings.Plans.FirstOrDefault(p => string.Equals(p.Id, tier, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, tier, StringComparison.OrdinalIgnoreCase));
                        if (plan != null)
                        {
                            revenue += (long)plan.MonthlyPrice;
                            var match = PlanBreakdown.FirstOrDefault(b => 
                                string.Equals(b.PlanId, plan.Id, StringComparison.OrdinalIgnoreCase) || 
                                string.Equals(b.PlanName, plan.Name, StringComparison.OrdinalIgnoreCase));
                            if (match != null) match.Count++;
                        }
                        else
                        {
                            freeTierItem.Count++;
                        }
                        activeSubsCount++;
                    }
                }

                freeTierItem.Count = await _context.Workspaces.CountAsync(w => string.IsNullOrEmpty(w.PackageTier) || w.PackageTier == "Free");
            }

            ProjectedMonthlyRevenue = revenue;
            ActiveSubscriptionsCount = activeSubsCount;

            BusinessSubscribers = PlanBreakdown.FirstOrDefault(b => string.Equals(b.PlanName, "Business", StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
            ProPlusSubscribers = PlanBreakdown.FirstOrDefault(b => string.Equals(b.PlanName, "Pro+", StringComparison.OrdinalIgnoreCase) || string.Equals(b.PlanName, "ProPlus", StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
            ProSubscribers = PlanBreakdown.FirstOrDefault(b => string.Equals(b.PlanName, "Pro", StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
            PersonalSubscribers = PlanBreakdown.FirstOrDefault(b => string.Equals(b.PlanName, "Personal", StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
            FreeTierWorkspaces = freeTierItem.Count;

            AverageRevenuePerSubscription = ActiveSubscriptionsCount > 0 ? (double)ProjectedMonthlyRevenue / ActiveSubscriptionsCount : 0.0;
            ProjectedNetProfit = ProjectedMonthlyRevenue - TotalCosts;

            // Generate Past 6 Months dynamic chart data
            var labels = new List<string>();
            var revenueData = new List<decimal>();
            var costData = new List<decimal>();
            var profitData = new List<decimal>();
            var chartStartMonth = new DateOnly(now.AddMonths(-5).Year, now.AddMonths(-5).Month, 1);
            var chartCosts = await _context.MonthlyProjectCosts
                .AsNoTracking()
                .Where(c => c.CostMonth >= chartStartMonth && !c.IsDisabled)
                .ToListAsync();
            var costsByMonth = chartCosts
                .GroupBy(c => c.CostMonth)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

            for (int i = 5; i >= 0; i--)
            {
                var monthDate = now.AddMonths(-i);
                labels.Add(monthDate.ToString("MMM yyyy"));

                var firstDayOfMonth = new DateTime(monthDate.Year, monthDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddSeconds(-1);

                decimal monthlyRev = 0;

                // Active billing subscriptions during this month
                foreach (var billing in allBillings)
                {
                    var billingCreated = billing.CreatedAt ?? billing.EndDate.AddYears(-1);
                    if (billingCreated <= lastDayOfMonth && billing.EndDate >= firstDayOfMonth)
                    {
                        var packageId = billing.PackageId.ToLower();
                        decimal amount = billing.Amount ?? 0;
                        if (amount == 0)
                        {
                            var parts = packageId.Split('_');
                            if (parts.Length > 0)
                            {
                                var planId = parts[0];
                                var isYearly = packageId.Contains("yearly");
                                var plan = settings.Plans.FirstOrDefault(p => string.Equals(p.Id, planId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, planId, StringComparison.OrdinalIgnoreCase));
                                if (plan != null)
                                {
                                    amount = isYearly ? plan.YearlyPrice / 12 : plan.MonthlyPrice;
                                }
                            }
                        }
                        else
                        {
                            if (packageId.Contains("yearly"))
                            {
                                amount = amount / 12;
                            }
                        }
                        monthlyRev += amount;
                    }
                }

                // Workspaces set directly (Free-tier exclusions)
                foreach (var ws in workspacesWithTierDirect)
                {
                    if (ws.CreatedAt <= lastDayOfMonth)
                    {
                        var tier = ws.PackageTier.ToLower();
                        var plan = settings.Plans.FirstOrDefault(p => string.Equals(p.Id, tier, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, tier, StringComparison.OrdinalIgnoreCase));
                        decimal wsAmount = plan != null ? plan.MonthlyPrice : 0;
                        monthlyRev += wsAmount;
                    }
                }

                var chartMonth = new DateOnly(monthDate.Year, monthDate.Month, 1);
                var monthlyCost = costsByMonth.GetValueOrDefault(chartMonth);
                if (chartMonth == selectedCostMonth && savedMonthlyCosts.Count == 0)
                {
                    monthlyCost = TotalCosts;
                }

                revenueData.Add(monthlyRev);
                costData.Add(monthlyCost);
                profitData.Add(monthlyRev - monthlyCost);
            }

            ChartLabelsJson = JsonSerializer.Serialize(labels);
            RevenueChartDataJson = JsonSerializer.Serialize(revenueData);
            CostChartDataJson = JsonSerializer.Serialize(costData);
            ProfitChartDataJson = JsonSerializer.Serialize(profitData);

            // Anonymous page-view traffic for the Admin dashboard.
            var trafficToday = DateOnly.FromDateTime(DateTime.UtcNow);
            var trafficStartDate = trafficToday.AddDays(-13);
            var recentTraffic = await _context.WebsiteTraffic
                .AsNoTracking()
                .Where(t => t.TrafficDate >= trafficStartDate)
                .ToListAsync();

            TrafficToday = recentTraffic
                .Where(t => t.TrafficDate == trafficToday)
                .Sum(t => t.VisitCount);
            TotalTraffic = await _context.WebsiteTraffic
                .AsNoTracking()
                .SumAsync(t => (long?)t.VisitCount) ?? 0;

            var trafficByDate = recentTraffic
                .GroupBy(t => t.TrafficDate)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.VisitCount));
            var trafficLabels = new List<string>();
            var trafficValues = new List<long>();
            for (var date = trafficStartDate; date <= trafficToday; date = date.AddDays(1))
            {
                trafficLabels.Add(date.ToString("dd/MM"));
                trafficValues.Add(trafficByDate.GetValueOrDefault(date));
            }

            TrafficChartLabelsJson = JsonSerializer.Serialize(trafficLabels);
            TrafficChartDataJson = JsonSerializer.Serialize(trafficValues);

            var topTrafficPages = await _context.WebsiteTraffic
                .AsNoTracking()
                .GroupBy(t => t.Path)
                .Select(g => new { Path = g.Key, VisitCount = g.Sum(t => t.VisitCount) })
                .OrderByDescending(t => t.VisitCount)
                .Take(5)
                .ToListAsync();
            TopTrafficPages = topTrafficPages.Select(t => new TopTrafficPageItem
            {
                Path = t.Path,
                VisitCount = t.VisitCount
            }).ToList();

            // Recent system activities (Audit Logs)
            RecentAuditLogs = await _context.AuditLogs
                .Include(l => l.User)
                .Include(l => l.Workspace)
                .Include(l => l.Federation)
                .OrderByDescending(l => l.Timestamp)
                .Take(8)
                .ToListAsync();

            return Page();
        }

        public async System.Threading.Tasks.Task<IActionResult> OnPostSaveFeesAsync()
        {
            if (!ModelState.IsValid)
            {
                StatusMessage = "Error: Invalid fee configurations.";
                return RedirectToPage();
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<OperationCostSetting> costs;
            try
            {
                costs = JsonSerializer.Deserialize<List<OperationCostSetting>>(OperationCostsJson, options) ?? new();
            }
            catch (JsonException)
            {
                StatusMessage = "Error: Invalid operational cost data.";
                return RedirectToPage(new { SelectedMonth = CostMonth });
            }

            if (!DateOnly.TryParseExact($"{CostMonth}-01", "yyyy-MM-dd", out var costMonth) ||
                costs.Any(c => string.IsNullOrWhiteSpace(c.Name) || c.Amount < 0))
            {
                StatusMessage = "Error: Invalid month or operational cost values.";
                return RedirectToPage(new { SelectedMonth = CostMonth });
            }

            var existingCosts = await _context.MonthlyProjectCosts
                .Where(c => c.CostMonth == costMonth)
                .ToListAsync();
            _context.MonthlyProjectCosts.RemoveRange(existingCosts);
            _context.MonthlyProjectCosts.AddRange(costs.Select(c => new MonthlyProjectCost
            {
                CostMonth = costMonth,
                Name = c.Name.Trim(),
                Amount = c.Amount,
                IsDisabled = c.IsDisabled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }));
            await _context.SaveChangesAsync();

            SaveSettings();
            StatusMessage = $"Success: Costs for {costMonth:MM/yyyy} saved successfully.";
            return RedirectToPage(new { SelectedMonth = CostMonth });
        }

        private static DateOnly ResolveMonth(string? selectedMonth, DateTime now)
        {
            if (!string.IsNullOrWhiteSpace(selectedMonth) && selectedMonth != "current" &&
                DateOnly.TryParseExact($"{selectedMonth}-01", "yyyy-MM-dd", out var parsed))
            {
                return parsed;
            }

            return new DateOnly(now.Year, now.Month, 1);
        }

        private void LoadSettings()
        {
            var settings = unigrid.Models.AdminSettings.Load(_context);
            OperationCostsJson = JsonSerializer.Serialize(settings.OperationCosts);
            PlansJson = JsonSerializer.Serialize(settings.Plans);
            TotalCosts = settings.OperationCosts.Where(c => !c.IsDisabled).Sum(c => c.Amount);
        }

        private void SaveSettings()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            // Monthly costs are stored in MonthlyProjectCosts. Keep the legacy
            // OperationCosts setting only as the initial template for this month.
            var settings = unigrid.Models.AdminSettings.Load(_context);

            if (!string.IsNullOrWhiteSpace(PlansJson))
            {
                try
                {
                    settings.Plans = JsonSerializer.Deserialize<List<PlanSetting>>(PlansJson, options) ?? new();
                }
                catch (JsonException)
                {
                    settings.Plans = unigrid.Models.AdminSettings.Load(_context).Plans;
                }
            }

            settings.Save(_context);
        }
    }
}
