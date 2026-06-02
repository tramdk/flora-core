using System.Collections.Generic;

namespace FloraCore.Application.Features.Orders.DTOs;

/// <summary>
/// DTO representing revenue statistics of the shop based on paid orders.
/// </summary>
public record RevenueStatisticsDto
{
    /// <summary>
    /// Gets the total revenue from paid orders.
    /// </summary>
    public decimal TotalRevenue { get; init; }

    /// <summary>
    /// Gets the total number of paid orders.
    /// </summary>
    public int TotalPaidOrders { get; init; }

    /// <summary>
    /// Gets the average revenue per paid order.
    /// </summary>
    public decimal AverageRevenuePerOrder { get; init; }

    /// <summary>
    /// Gets the revenue grouped by period (e.g. "yyyy-MM-dd", "yyyy-MM", "yyyy").
    /// </summary>
    public Dictionary<string, decimal> RevenueByPeriod { get; init; } = new();
}
