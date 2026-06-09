using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Features.Products.Commands;
using FloraCore.Domain.Entities;
using FluentAssertions;
using MediatR;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FloraCore.Tests.Application.Features.Products.Commands;

public class UpdateProductCommandHandlerTests
{
    private readonly Mock<IGenericRepository<Product, Guid>> _mockProductRepository;
    private readonly UpdateProductCommandHandler _handler;

    public UpdateProductCommandHandlerTests()
    {
        _mockProductRepository = new Mock<IGenericRepository<Product, Guid>>();
        _handler = new UpdateProductCommandHandler(_mockProductRepository.Object);
    }

    [Fact]
    public async Task Handle_Should_UpdateProductWithPromotionRate_When_ProductExists()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var existingProduct = new Product
        {
            Id = productId,
            Name = "Old Name",
            Description = "Old Description",
            Price = 50m,
            PromotionRate = 0m,
            Stock = 5,
            ImageUrl = "old.jpg",
            CategoryId = null
        };

        _mockProductRepository.Setup(x => x.GetByIdAsync(productId)).ReturnsAsync(existingProduct);

        var command = new UpdateProductCommand(
            Id: productId,
            Name: "New Name",
            Description: "New Description",
            Price: 100m,
            PromotionRate: 20m,
            Stock: 10,
            ImageUrl: "new.jpg",
            CategoryId: Guid.NewGuid()
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(Unit.Value);
        existingProduct.Name.Should().Be(command.Name);
        existingProduct.Description.Should().Be(command.Description);
        existingProduct.Price.Should().Be(command.Price);
        existingProduct.PromotionRate.Should().Be(command.PromotionRate);
        existingProduct.Stock.Should().Be(command.Stock);
        existingProduct.ImageUrl.Should().Be(command.ImageUrl);
        existingProduct.CategoryId.Should().Be(command.CategoryId);

        _mockProductRepository.Verify(x => x.UpdateAsync(existingProduct), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ThrowException_When_ProductDoesNotExist()
    {
        // Arrange
        var productId = Guid.NewGuid();
        _mockProductRepository.Setup(x => x.GetByIdAsync(productId)).ReturnsAsync((Product?)null);

        var command = new UpdateProductCommand(
            Id: productId,
            Name: "New Name",
            Description: "New Description",
            Price: 100m,
            PromotionRate: 20m,
            Stock: 10,
            ImageUrl: "new.jpg",
            CategoryId: Guid.NewGuid()
        );

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FloraCore.Domain.Exceptions.ProductNotFoundException>()
            .WithMessage($"Product with ID '{productId}' was not found.");
        _mockProductRepository.Verify(x => x.UpdateAsync(It.IsAny<Product>()), Times.Never);
    }
}
