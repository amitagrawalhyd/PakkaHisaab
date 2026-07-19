namespace PakkaHisaab.Admin.Models;

/// <summary>View model for the Shared/_StatCard partial (dashboard KPI tiles).</summary>
public sealed class StatCardModel
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string? Sub { get; init; }
    public string Icon { get; init; } = "bi-graph-up";
    /// <summary>One of: teal, green, navy, amber — matches .ph-stat-icon.* in site.css.</summary>
    public string Color { get; init; } = "teal";
}

/// <summary>View model for the Shared/_Pagination partial.</summary>
public sealed class PaginationModel
{
    public required int Page { get; init; }
    public required int TotalPages { get; init; }
    /// <summary>Query string (minus "page") to preserve active filters across page links.</summary>
    public string QueryString { get; init; } = "";
}
