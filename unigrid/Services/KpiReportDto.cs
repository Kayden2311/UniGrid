using System;
using System.Collections.Generic;

namespace unigrid.Services;

public class KpiReportDto
{
    public string PeriodType { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<KpiCategoryDto> Categories { get; set; } = new List<KpiCategoryDto>();
    public List<MemberPerformanceDto> MemberPerformances { get; set; } = new List<MemberPerformanceDto>();
}

public class KpiCategoryDto
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = null!;
    public string ColorHex { get; set; } = "#3B82F6";
}

public class MemberPerformanceDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public List<KpiDetailDto> KpiDetails { get; set; } = new List<KpiDetailDto>();
    public double TotalAchievementRate { get; set; }
}

public class KpiDetailDto
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = null!;
    public int TargetValue { get; set; }
    public int ActualValue { get; set; }
    public double AchievementRate { get; set; }
}
