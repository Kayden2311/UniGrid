using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using unigrid.Data;

namespace unigrid.Models
{
    public class OperationCostSetting
    {
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsDisabled { get; set; } = false;
    }

    public class PlanSetting
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ColorClass { get; set; } = "indigo"; // teal, indigo, violet, emerald, slate
        public List<string> Features { get; set; } = new();
        public int MemberLimit { get; set; } = 5;
        public string StorageLimit { get; set; } = "No File Storage";
        public int ChatLimit { get; set; } = 0;
        public int TaskBranchLimit { get; set; } = 1;
        public bool HasAdvancedAnalytics { get; set; } = false;
        public bool HasRolePermissions { get; set; } = false;
        public bool IsDisabled { get; set; } = false;
    }

    public class AdminSettings
    {
        public List<OperationCostSetting> OperationCosts { get; set; } = new();
        public List<PlanSetting> Plans { get; set; } = new();

        public static PlanSetting GetPlanSetting(string? tier, UniGridDbContext? context = null)
        {
            if (string.IsNullOrEmpty(tier) || tier.Equals("Free", StringComparison.OrdinalIgnoreCase))
            {
                return new PlanSetting
                {
                    Id = "Free",
                    Name = "Free",
                    MemberLimit = 5,
                    StorageLimit = "0 GB Storage",
                    ChatLimit = 1,
                    TaskBranchLimit = 1,
                    HasAdvancedAnalytics = false,
                    HasRolePermissions = false
                };
            }
            
            var settings = Load(context);
            var plan = settings.Plans.FirstOrDefault(p => p.Id.Equals(tier, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(tier, StringComparison.OrdinalIgnoreCase));
            if (plan == null)
            {
                return new PlanSetting
                {
                    Id = "Free",
                    Name = "Free",
                    MemberLimit = 5,
                    StorageLimit = "0 GB Storage",
                    ChatLimit = 1,
                    TaskBranchLimit = 1,
                    HasAdvancedAnalytics = false,
                    HasRolePermissions = false
                };
            }
            return plan;
        }

        public static AdminSettings Load(UniGridDbContext? context = null)
        {
            bool createdContext = false;
            if (context == null)
            {
                try
                {
                    context = new UniGridDbContext();
                    createdContext = true;
                }
                catch
                {
                    return LoadFromFallbackSources();
                }
            }

            try
            {
                var settings = new AdminSettings();
                var opCostsSetting = context.SystemSettings.FirstOrDefault(s => s.SettingKey == "OperationCosts");
                var plansSetting = context.SystemSettings.FirstOrDefault(s => s.SettingKey == "Plans");

                bool needsSave = false;
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                if (opCostsSetting == null && plansSetting == null)
                {
                    // Attempt to migrate from admin-settings.json if it exists
                    var path = Path.Combine(Directory.GetCurrentDirectory(), "admin-settings.json");
                    if (File.Exists(path))
                    {
                        try
                        {
                            var json = File.ReadAllText(path);
                            var fileSettings = JsonSerializer.Deserialize<AdminSettings>(json, options);
                            if (fileSettings != null)
                            {
                                settings.OperationCosts = fileSettings.OperationCosts ?? GetDefaultCosts();
                                settings.Plans = fileSettings.Plans ?? GetDefaultPlans();
                                settings.Save(context);
                                return settings;
                            }
                        }
                        catch
                        {
                            // Ignore and let it fallback to defaults
                        }
                    }
                }

                if (opCostsSetting != null && !string.IsNullOrEmpty(opCostsSetting.SettingValue))
                {
                    settings.OperationCosts = JsonSerializer.Deserialize<List<OperationCostSetting>>(opCostsSetting.SettingValue, options) ?? new();
                }
                else
                {
                    settings.OperationCosts = GetDefaultCosts();
                    needsSave = true;
                }

                if (plansSetting != null && !string.IsNullOrEmpty(plansSetting.SettingValue))
                {
                    settings.Plans = JsonSerializer.Deserialize<List<PlanSetting>>(plansSetting.SettingValue, options) ?? new();
                }
                else
                {
                    settings.Plans = GetDefaultPlans();
                    needsSave = true;
                }

                if (needsSave)
                {
                    settings.Save(context);
                }

                return settings;
            }
            catch
            {
                return LoadFromFallbackSources();
            }
            finally
            {
                if (createdContext && context != null)
                {
                    context.Dispose();
                }
            }
        }

        private static AdminSettings LoadFromFallbackSources()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "admin-settings.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var settings = JsonSerializer.Deserialize<AdminSettings>(json, options);
                    if (settings != null)
                    {
                        if (settings.OperationCosts == null || !settings.OperationCosts.Any())
                        {
                            settings.OperationCosts = GetDefaultCosts();
                        }
                        if (settings.Plans == null || !settings.Plans.Any())
                        {
                            settings.Plans = GetDefaultPlans();
                        }
                        return settings;
                    }
                }
                catch
                {
                    // Fallback to default
                }
            }

            return new AdminSettings
            {
                OperationCosts = GetDefaultCosts(),
                Plans = GetDefaultPlans()
            };
        }

        public void Save(UniGridDbContext? context = null)
        {
            // Always save to file as a backup
            try
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "admin-settings.json");
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignore backup save errors
            }

            bool createdContext = false;
            if (context == null)
            {
                try
                {
                    context = new UniGridDbContext();
                    createdContext = true;
                }
                catch
                {
                    return; // Can't write to DB
                }
            }

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var opCostsJson = JsonSerializer.Serialize(this.OperationCosts, options);
                var plansJson = JsonSerializer.Serialize(this.Plans, options);

                var opCostsSetting = context.SystemSettings.FirstOrDefault(s => s.SettingKey == "OperationCosts");
                if (opCostsSetting == null)
                {
                    context.SystemSettings.Add(new SystemSetting
                    {
                        SettingKey = "OperationCosts",
                        SettingValue = opCostsJson,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    opCostsSetting.SettingValue = opCostsJson;
                    opCostsSetting.UpdatedAt = DateTime.UtcNow;
                }

                var plansSetting = context.SystemSettings.FirstOrDefault(s => s.SettingKey == "Plans");
                if (plansSetting == null)
                {
                    context.SystemSettings.Add(new SystemSetting
                    {
                        SettingKey = "Plans",
                        SettingValue = plansJson,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    plansSetting.SettingValue = plansJson;
                    plansSetting.UpdatedAt = DateTime.UtcNow;
                }

                context.SaveChanges();
            }
            catch
            {
                // Ignore DB save errors
            }
            finally
            {
                if (createdContext && context != null)
                {
                    context.Dispose();
                }
            }
        }

        private static List<OperationCostSetting> GetDefaultCosts()
        {
            return new List<OperationCostSetting>
            {
                new() { Name = "Server & Infrastructure", Amount = 1500000 },
                new() { Name = "Cloud Storage & Database", Amount = 2500000 },
                new() { Name = "AI APIs Usage", Amount = 1800000 }
            };
        }

        private static List<PlanSetting> GetDefaultPlans()
        {
            return new List<PlanSetting>
            {
                new() { 
                    Id = "Personal", 
                    Name = "Personal", 
                    MonthlyPrice = 40000, 
                    YearlyPrice = 399000, 
                    Description = "Dedicated solo power workspace", 
                    ColorClass = "teal",
                    Features = new() { "1 Workspace", "1 Member", "2 GB Storage (Individual)", "0 Chat Channels", "1 Task Branch", "Basic Analytics" },
                    MemberLimit = 1,
                    StorageLimit = "2 GB Storage (Individual)",
                    ChatLimit = 0,
                    TaskBranchLimit = 1,
                    HasAdvancedAnalytics = false,
                    HasRolePermissions = false
                },
                new() { 
                    Id = "Pro", 
                    Name = "Pro", 
                    MonthlyPrice = 299000, 
                    YearlyPrice = 2900000, 
                    Description = "More power for growing teams", 
                    ColorClass = "indigo",
                    Features = new() { "1 Workspace", "10 Members", "20 GB Storage", "3 Chat Channels", "2 Task Branches", "Basic Analytics" },
                    MemberLimit = 10,
                    StorageLimit = "20 GB Storage",
                    ChatLimit = 3,
                    TaskBranchLimit = 2,
                    HasAdvancedAnalytics = false,
                    HasRolePermissions = false
                },
                new() { 
                    Id = "ProPlus", 
                    Name = "Pro+", 
                    MonthlyPrice = 449000, 
                    YearlyPrice = 4400000, 
                    Description = "Advanced features for power users", 
                    ColorClass = "violet",
                    Features = new() { "1 Workspace", "15 Members", "40 GB Storage", "5 Chat Channels", "4 Task Branches", "Advanced Analytics", "Role Permissions" },
                    MemberLimit = 15,
                    StorageLimit = "40 GB Storage",
                    ChatLimit = 5,
                    TaskBranchLimit = 4,
                    HasAdvancedAnalytics = true,
                    HasRolePermissions = true
                },
                new() { 
                    Id = "Business", 
                    Name = "Business", 
                    MonthlyPrice = 899000, 
                    YearlyPrice = 8900000, 
                    Description = "Unlimited power for organizations", 
                    ColorClass = "slate",
                    Features = new() { "1 Workspace", "30 Members", "80 GB Storage", "Unlimited Chat Channels", "Unlimited Task Branches", "Advanced Analytics", "Role Permissions" },
                    MemberLimit = 30,
                    StorageLimit = "80 GB Storage",
                    ChatLimit = -1,
                    TaskBranchLimit = -1,
                    HasAdvancedAnalytics = true,
                    HasRolePermissions = true
                }
            };
        }
    }
}
