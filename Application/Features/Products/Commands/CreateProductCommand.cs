using System;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;
using UUIDNext;

namespace FloraCore.Application.Features.Products.Commands;

public record CreateProductCommand(Guid? Id, string Name, string Description, decimal Price,
        decimal PromotionRate, int Stock, string? ImageUrl, Guid? CategoryId) : IRequest<Guid>;

public class CreateProductCommandHandler(IGenericRepository<Product, Guid> productRepository) : IRequestHandler<CreateProductCommand, Guid>
{
    private readonly IGenericRepository<Product, Guid> _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));

    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = new Product
        {
            Id = request.Id ?? Uuid.NewDatabaseFriendly(Database.PostgreSql),
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            PromotionRate = request.PromotionRate,
            Stock = request.Stock,
            ImageUrl = request.ImageUrl,
            CategoryId = request.CategoryId
        };

        await _productRepository.AddAsync(product);
        return product.Id;
    }
}
