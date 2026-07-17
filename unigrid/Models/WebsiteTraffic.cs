namespace unigrid.Models;

/// <summary>
/// Aggregated page-view traffic. No visitor identity, IP address or cookie is stored.
/// </summary>
public class WebsiteTraffic
{
    public DateOnly TrafficDate { get; set; }

    public string Path { get; set; } = "/";

    public long VisitCount { get; set; }

    public DateTime UpdatedAt { get; set; }
}
