using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Features.Orders.DTOs;
using FloraCore.Application.Features.Orders.Queries;
using FloraCore.Domain.Constants;
using FloraCore.Domain.Entities;
using FloraCore.Tests;
using FluentAssertions;
using Moq;
using Xunit;

namespace FloraCore.Tests.Application.Features.Orders.Queries;

public class GetRevenueStatisticsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnEmptyStatistics_WhenNoPaidOrdersExist()
    {
        // Arrange
        var mockOrderRepository = new Mock<IGenericRepository<Order, Guid>>();
        mockOrderRepository.Setup(repo => repo.GetQueryable()).Returns(new List<Order>().AsAsyncQueryable());
        var handler = new GetRevenueStatisticsQueryHandler(mockOrderRepository.Object);
        var query = new GetRevenueStatisticsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalRevenue.Should().Be(0);
        result.TotalPaidOrders.Should().Be(0);
        result.AverageRevenuePerOrder.Should().Be(0);
        result.RevenueByPeriod.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldOnlyIncludePaidOrders()
    {
        // Arrange
        var orders = new List<Order>
        {
            new Order { Id = Guid.NewGuid(), OrderDate = DateTime.Now, PaymentStatus = PaymentStatus.Paid, TotalAmount = 100 },
            new Order { Id = Guid.NewGuid(), OrderDate = DateTime.Now, PaymentStatus = PaymentStatus.Pending, TotalAmount = 200 },
            new Order { Id = Guid.NewGuid(), OrderDate = DateTime.Now, PaymentStatus = PaymentStatus.Failed, TotalAmount = 300 }
        };
        var mockOrderRepository = new Mock<IGenericRepository<Order, Guid>>();
        mockOrderRepository.Setup(repo => repo.GetQueryable()).Returns(orders.AsAsyncQueryable());
        var handler = new GetRevenueStatisticsQueryHandler(mockOrderRepository.Object);
        var query = new GetRevenueStatisticsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalPaidOrders.Should().Be(1);
        result.TotalRevenue.Should().Be(100);
        result.AverageRevenuePerOrder.Should().Be(100);
    }

    [Fact]
    public async Task Handle_ShouldFilterByStartDate_WhenProvided()
    {
        // Arrange
        var today = DateTime.Today;
        var orders = new List<Order>
        {
            new Order { Id = Guid.NewGuid(), OrderDate = today.AddDays(-1), PaymentStatus = PaymentStatus.Paid, TotalAmount = 100 },
            new Order { Id = Guid.NewGuid(), OrderDate = today, PaymentStatus = PaymentStatus.Paid, TotalAmount = 200 }
        };
        var mockOrderRepository = new Mock<IGenericRepository<Order, Guid>>();
        mockOrderRepository.Setup(repo => repo.GetQueryable()).Returns(orders.AsAsyncQueryable());
        var handler = new GetRevenueStatisticsQueryHandler(mockOrderRepository.Object);
        var query = new GetRevenueStatisticsQuery { StartDate = today };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalPaidOrders.Should().Be(1);
        result.TotalRevenue.Should().Be(200);
    }

    [Fact]
    public async Task Handle_ShouldFilterByEndDate_WhenProvided()
    {
        // Arrange
        var today = DateTime.Today;
        var orders = new List<Order>
        {
            new Order { Id = Guid.NewGuid(), OrderDate = today.AddDays(-1), PaymentStatus = PaymentStatus.Paid, TotalAmount = 100 },
            new Order { Id = Guid.NewGuid(), OrderDate = today, PaymentStatus = PaymentStatus.Paid, TotalAmount = 200 }
        };
        var mockOrderRepository = new Mock<IGenericRepository<Order, Guid>>();
        mockOrderRepository.Setup(repo => repo.GetQueryable()).Returns(orders.AsAsyncQueryable());
        var handler = new GetRevenueStatisticsQueryHandler(mockOrderRepository.Object);
        var query = new GetRevenueStatisticsQuery { EndDate = today.AddDays(-1) };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalPaidOrders.Should().Be(1);
        result.TotalRevenue.Should().Be(100);
    }

    [Fact]
    public async Task Handle_ShouldGroupByDaily_WhenRequested()
    {
        // Arrange
        var orders = new List<Order>
        {
            new Order { Id = Guid.NewGuid(), OrderDate = new DateTime(2026, 06, 01, 10, 0, 0), PaymentStatus = PaymentStatus.Paid, TotalAmount = 100 },
            new Order { Id = Guid.NewGuid(), OrderDate = new DateTime(2026, 06, 01, 15, 0, 0), PaymentStatus = PaymentStatus.Paid, TotalAmount = 150 },
            new Order { Id = Guid.NewGuid(), OrderDate = new DateTime(2026, 06, 02, 11, 0, 0), PaymentStatus = PaymentStatus.Paid, TotalAmount = 200 }
        };
        var mockOrderRepository = new Mock<IGenericRepository<Order, Guid>>();
        mockOrderRepository.Setup(repo => repo.GetQueryable()).Returns(orders.AsAsyncQueryable());
        var handler = new GetRevenueStatisticsQueryHandler(mockOrderRepository.Object);
        var query = new GetRevenueStatisticsQuery { GroupBy = "day" };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.RevenueByPeriod.Should().ContainKey("2026-06-01").WhoseValue.Should().Be(250);
        result.RevenueByPeriod.Should().ContainKey("2026-06-02").WhoseValue.Should().Be(200);
    }

    [Fact]
    public async Task Handle_ShouldGroupByYearly_WhenRequested()
    {
        // Arrange
        var orders = new List<Order>
        {
            new Order { Id = Guid.NewGuid(), OrderDate = new DateTime(2025, 06, 01), PaymentStatus = PaymentStatus.Paid, TotalAmount = 100 },
            new Order { Id = Guid.NewGuid(), OrderDate = new DateTime(2026, 06, 01), PaymentStatus = PaymentStatus.Paid, TotalAmount = 250 }
        };
        var mockOrderRepository = new Mock<IGenericRepository<Order, Guid>>();
        mockOrderRepository.Setup(repo => repo.GetQueryable()).Returns(orders.AsAsyncQueryable());
        var handler = new GetRevenueStatisticsQueryHandler(mockOrderRepository.Object);
        var query = new GetRevenueStatisticsQuery { GroupBy = "year" };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.RevenueByPeriod.Should().ContainKey("2025").WhoseValue.Should().Be(100);
        result.RevenueByPeriod.Should().ContainKey("2026").WhoseValue.Should().Be(250);
    }
}
