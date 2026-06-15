using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Features.Cart.Queries;
using FloraCore.Application.Features.Cart.DTOs;
using FloraCore.Application.Features.Cart.DTOs;
using FloraCore.Domain.Entities;
using FloraCore.Infrastructure.Data;
using FloraCore.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FloraCore.Tests.Application.Features.Cart.Queries;

public class GetCartQueryHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly Mock<ICurrentUserService> _mockCurrentUserService;
    private readonly Mock<IResourceManager> _mockResourceManager;
    private readonly GenericRepository<FloraCore.Domain.Entities.Cart, Guid> _cartRepository;

    public GetCartQueryHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _mockCurrentUserService = new Mock<ICurrentUserService>();
        _mockResourceManager = new Mock<IResourceManager>();
        _mockResourceManager.Setup(x => x.GetString("UserNotAuthenticated")).Returns("User not authenticated");
        _cartRepository = new GenericRepository<FloraCore.Domain.Entities.Cart, Guid>(_context);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyCart_WhenCartDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockCurrentUserService.Setup(x => x.UserId).Returns(userId);

        var handler = new GetCartQueryHandler(_cartRepository, _mockCurrentUserService.Object, _mockResourceManager.Object);
        var query = new GetCartQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(userId);
        result.Items.Should().BeEmpty();
        result.TotalPrice.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ShouldReturnCartWithDiscountedPrices_WhenProductsHavePromotionRates()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockCurrentUserService.Setup(x => x.UserId).Returns(userId);

        var user = new AppUser
        {
            Id = userId,
            UserName = "testuser",
            Email = "testuser@example.com",
            FullName = "Test User"
        };
        _context.Users.Add(user);

        // Seed products
        var product1 = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Product 1",
            Price = 100m,
            PromotionRate = 10m, // 10% off -> Price should be 90
            Stock = 10
        };

        var product2 = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Product 2",
            Price = 200m,
            PromotionRate = 25m, // 25% off -> Price should be 150
            Stock = 5
        };

        _context.Products.AddRange(product1, product2);

        // Seed Cart
        var cart = new FloraCore.Domain.Entities.Cart
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Items = new List<CartItem>
            {
                new() { Id = Guid.NewGuid(), ProductId = product1.Id, Quantity = 2 },
                new() { Id = Guid.NewGuid(), ProductId = product2.Id, Quantity = 1 }
            }
        };

        _context.Carts.Add(cart);
        await _context.SaveChangesAsync();

        var handler = new GetCartQueryHandler(_cartRepository, _mockCurrentUserService.Object, _mockResourceManager.Object);
        var query = new GetCartQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(userId);
        result.Items.Should().HaveCount(2);

        var item1 = result.Items.First(i => i.ProductId == product1.Id);
        item1.OriginalPrice.Should().Be(100m);
        item1.PromotionRate.Should().Be(10m);
        item1.Price.Should().Be(90m);
        item1.Quantity.Should().Be(2);

        var item2 = result.Items.First(i => i.ProductId == product2.Id);
        item2.OriginalPrice.Should().Be(200m);
        item2.PromotionRate.Should().Be(25m);
        item2.Price.Should().Be(150m);
        item2.Quantity.Should().Be(1);

        // TotalPrice: (90 * 2) + (150 * 1) = 180 + 150 = 330
        result.TotalPrice.Should().Be(330m);
    }

    [Fact]
    public async Task Handle_ShouldThrowException_WhenUserNotAuthenticated()
    {
        // Arrange
        _mockCurrentUserService.Setup(x => x.UserId).Returns((Guid?)null);

        var handler = new GetCartQueryHandler(_cartRepository, _mockCurrentUserService.Object, _mockResourceManager.Object);
        var query = new GetCartQuery();

        // Act
        var act = () => handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("User not authenticated");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
