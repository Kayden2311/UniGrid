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
    [Authorize(Roles = "1")]
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

        // Operational Cost Settings
        [BindProperty]
        public decimal ServerFee { get; set; }

        [BindProperty]
        public decimal CloudFee { get; set; }

        [BindProperty]
        public decimal AiApiFee { get; set; }

        public decimal TotalCosts { get; set; }
        public decimal ProjectedNetProfit { get; set; }

        // Chart Data (past 6 months)
        public string ChartLabelsJson { get; set; } = "[]";
        public string RevenueChartDataJson { get; set; } = "[]";
        public string CostChartDataJson { get; set; } = "[]";
        public string ProfitChartDataJson { get; set; } = "[]";

        [TempData]
        public string? StatusMessage { get; set; }

        public async System.Threading.Tasks.Task<IActionResult> OnGetAsync()
        {
            // Load fee configurations
            LoadSettings();

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

            var activeBillings = allBillings.Where(b => b.Status == "Active").ToList();
            ActiveSubscriptionsCount = activeBillings.Count;

            long revenue = 0;
            int business = 0, proplus = 0, pro = 0, personal = 0, free = 0;

            // Project monthly revenue from active billing records
            foreach (var billing in activeBillings)
            {
                var packageId = billing.PackageId.ToLower();
                
                // Use actual billing amount if stored, otherwise fall back to package tier estimation
                decimal itemAmount = billing.Amount ?? 0;
                if (itemAmount == 0)
                {
                    if (packageId.Contains("business")) itemAmount = packageId.Contains("yearly") ? 8900000 / 12 : 899000;
                    else if (packageId.Contains("proplus")) itemAmount = packageId.Contains("yearly") ? 4400000 / 12 : 449000;
                    else if (packageId.Contains("pro")) itemAmount = packageId.Contains("yearly") ? 2900000 / 12 : 299000;
                    else if (packageId.Contains("personal")) itemAmount = packageId.Contains("yearly") ? 399000 / 12 : 40000;
                }
                else
                {
                    // If it is a yearly billing, convert it to monthly projection for MRR
                    if (packageId.Contains("yearly"))
                    {
                        itemAmount = itemAmount / 12;
                    }
                }

                revenue += (long)itemAmount;

                if (packageId.Contains("business")) business++;
                else if (packageId.Contains("proplus")) proplus++;
                else if (packageId.Contains("pro")) pro++;
                else if (packageId.Contains("personal")) personal++;
                else free++;
            }

            // Also account for workspaces that have a PackageTier set directly but might not have a Billing record
            var workspacesWithTierDirect = await _context.Workspaces
                .Where(w => !string.IsNullOrEmpty(w.PackageTier) && w.PackageTier != "Free")
                .ToListAsync();

            foreach (var ws in workspacesWithTierDirect)
            {
                // If this workspace doesn't have an active billing record, count its revenue based on PackageTier
                if (!activeBillings.Any(b => b.WorkspaceId == ws.Id))
                {
                    var tier = ws.PackageTier.ToLower();
                    if (tier == "business")
                    {
                        revenue += 899000;
                        business++;
                        ActiveSubscriptionsCount++;
                    }
                    else if (tier == "proplus")
                    {
                        revenue += 449000;
                        proplus++;
                        ActiveSubscriptionsCount++;
                    }
                    else if (tier == "pro")
                    {
                        revenue += 299000;
                        pro++;
                        ActiveSubscriptionsCount++;
                    }
                    else if (tier == "personal")
                    {
                        revenue += 40000;
                        personal++;
                        ActiveSubscriptionsCount++;
                    }
                }
            }

            // Count actual free workspaces
            FreeTierWorkspaces = await _context.Workspaces.CountAsync(w => string.IsNullOrEmpty(w.PackageTier) || w.PackageTier == "Free");

            ProjectedMonthlyRevenue = revenue;
            BusinessSubscribers = business;
            ProPlusSubscribers = proplus;
            ProSubscribers = pro;
            PersonalSubscribers = personal;

            AverageRevenuePerSubscription = ActiveSubscriptionsCount > 0 ? (double)ProjectedMonthlyRevenue / ActiveSubscriptionsCount : 0.0;

            // Calculations for Costs & Net Profit
            TotalCosts = ServerFee + CloudFee + AiApiFee;
            ProjectedNetProfit = ProjectedMonthlyRevenue - TotalCosts;

            // Generate Past 6 Months dynamic chart data
            var labels = new List<string>();
            var revenueData = new List<decimal>();
            var costData = new List<decimal>();
            var profitData = new List<decimal>();

            var now = DateTime.UtcNow;
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
                            if (packageId.Contains("business")) amount = packageId.Contains("yearly") ? 8900000 / 12 : 899000;
                            else if (packageId.Contains("proplus")) amount = packageId.Contains("yearly") ? 4400000 / 12 : 449000;
                            else if (packageId.Contains("pro")) amount = packageId.Contains("yearly") ? 2900000 / 12 : 299000;
                            else if (packageId.Contains("personal")) amount = packageId.Contains("yearly") ? 399000 / 12 : 40000;
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
                        decimal wsAmount = 0;
                        if (tier == "business") wsAmount = 899000;
                        else if (tier == "proplus") wsAmount = 449000;
                        else if (tier == "pro") wsAmount = 299000;
                        else if (tier == "personal") wsAmount = 40000;

                        monthlyRev += wsAmount;
                    }
                }

                revenueData.Add(monthlyRev);
                costData.Add(TotalCosts); // Constant operational fee
                profitData.Add(monthlyRev - TotalCosts); // Profit = Rev - Cost
            }

            ChartLabelsJson = JsonSerializer.Serialize(labels);
            RevenueChartDataJson = JsonSerializer.Serialize(revenueData);
            CostChartDataJson = JsonSerializer.Serialize(costData);
            ProfitChartDataJson = JsonSerializer.Serialize(profitData);

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

            SaveSettings();
            StatusMessage = "Success: Operational fee settings saved successfully.";
            return RedirectToPage();
        }

        private string GetSettingsFilePath()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "admin-settings.json");
        }

        private void LoadSettings()
        {
            var path = GetSettingsFilePath();
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<AdminSettings>(json);
                    if (settings != null)
                    {
                        ServerFee = settings.ServerFee;
                        CloudFee = settings.CloudFee;
                        AiApiFee = settings.AiApiFee;
                        return;
                    }
                }
                catch
                {
                    // Fall back to defaults on parse exception
                }
            }

            ServerFee = 1500000;
            CloudFee = 2500000;
            AiApiFee = 1800000;
        }

        private void SaveSettings()
        {
            var path = GetSettingsFilePath();
            var settings = new AdminSettings
            {
                ServerFee = ServerFee,
                CloudFee = CloudFee,
                AiApiFee = AiApiFee
            };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);
        }

        public class AdminSettings
        {
            public decimal ServerFee { get; set; }
            public decimal CloudFee { get; set; }
            public decimal AiApiFee { get; set; }
        }
    }
}
