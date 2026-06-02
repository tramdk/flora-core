using System;
using FloraCore.Application.Features.Orders.DTOs;
using MediatR;

namespace FloraCore.Application.Features.Orders.Queries;

/// <summary>
/// Query to retrieve shop revenue statistics based on paid orders.
/// </summary>
public record GetRevenueStatisticsQuery : IRequest<RevenueStatisticsDto>
{
    /// <summary>
    /// Gets or sets the optional start date to filter orders.
    /// </summary>
    public DateTime? StartDate { get; init; }

    /// <summary>
    /// Gets or sets the optional end date to filter orders.
    /// </summary>
    public DateTime? EndDate { get; init; }

    /// <summary>
    /// Gets or sets the grouping period. Supported values: "day", "month", "year". Default is "month".
    /// </summary>
    public string GroupBy { get; init; } = "month";
}
