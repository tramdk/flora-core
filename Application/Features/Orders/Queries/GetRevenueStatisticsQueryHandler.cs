using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Features.Orders.DTOs;
using FloraCore.Domain.Constants;
using FloraCore.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FloraCore.Application.Features.Orders.Queries;

/// <summary>
/// Handler for <see cref="GetRevenueStatisticsQuery"/> to retrieve shop revenue statistics based on paid orders.
/// </summary>
public class GetRevenueStatisticsQueryHandler(IGenericRepository<Order, Guid> orderRepository)
    : IRequestHandler<GetRevenueStatisticsQuery, RevenueStatisticsDto>
{
    private readonly IGenericRepository<Order, Guid> _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));

    /// <inheritdoc />
    public async Task<RevenueStatisticsDto> Handle(GetRevenueStatisticsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _orderRepository.GetQueryable()
            .AsNoTracking()
            .Where(o => o.PaymentStatus == PaymentStatus.Paid);

        if (request.StartDate.HasValue)
        {
            query = query.Where(o => o.OrderDate >= request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            query = query.Where(o => o.OrderDate <= request.EndDate.Value);
        }

        // Project necessary columns to prevent over-fetching and memory bloat.
        var paidOrders = await query
            .Select(o => new
            {
                o.TotalAmount,
                o.OrderDate
            })
            .ToListAsync(cancellationToken);

        if (!paidOrders.Any())
        {
            return new RevenueStatisticsDto
            {
                TotalRevenue = 0,
                TotalPaidOrders = 0,
                AverageRevenuePerOrder = 0,
                RevenueByPeriod = new Dictionary<string, decimal>()
            };
        }

        var totalRevenue = paidOrders.Sum(o => o.TotalAmount);
        var totalPaidOrders = paidOrders.Count;
        var averageRevenuePerOrder = totalPaidOrders > 0 ? totalRevenue / totalPaidOrders : 0;

        // Group by period based on request
        var groupByNormalized = request.GroupBy?.ToLowerInvariant() ?? "month";
        Dictionary<string, decimal> revenueByPeriod;

        if (groupByNormalized is "day" or "daily" or "yyyy-MM-dd")
        {
            revenueByPeriod = paidOrders
                .GroupBy(o => o.OrderDate.Date)
                .OrderBy(g => g.Key)
                .ToDictionary(
                    g => g.Key.ToString("yyyy-MM-dd"),
                    g => g.Sum(o => o.TotalAmount)
                );
        }
        else if (groupByNormalized is "year" or "yearly" or "yyyy")
        {
            revenueByPeriod = paidOrders
                .GroupBy(o => o.OrderDate.Year)
                .OrderBy(g => g.Key)
                .ToDictionary(
                    g => g.Key.ToString(),
                    g => g.Sum(o => o.TotalAmount)
                );
        }
        else // default to month/monthly
        {
            revenueByPeriod = paidOrders
                .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .ToDictionary(
                    g => $"{g.Key.Year}-{g.Key.Month:D2}",
                    g => g.Sum(o => o.TotalAmount)
                );
        }

        return new RevenueStatisticsDto
        {
            TotalRevenue = totalRevenue,
            TotalPaidOrders = totalPaidOrders,
            AverageRevenuePerOrder = averageRevenuePerOrder,
            RevenueByPeriod = revenueByPeriod
        };
    }
}
