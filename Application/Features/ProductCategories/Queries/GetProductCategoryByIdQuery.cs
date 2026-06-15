using FloraCore.Domain.Entities;
using FloraCore.Application.Common.Interfaces;
using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;

using FloraCore.Application.Features.ProductCategories.DTOs;

using FloraCore.Application.Features.ProductCategories.DTOs;

namespace FloraCore.Application.Features.ProductCategories.Queries;

public record GetProductCategoryByIdQuery(Guid Id) : IRequest<ProductCategoryDto?>
{
    // ThrowIfNull
}

public class GetProductCategoryByIdQueryHandler(IGenericRepository<ProductCategory, Guid> repository) : IRequestHandler<GetProductCategoryByIdQuery, ProductCategoryDto?>
{
    private readonly IGenericRepository<ProductCategory, Guid> _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<ProductCategoryDto?> Handle(GetProductCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        var c = await _repository.GetByIdAsync(request.Id);
        if (c == null) return null;

        return new ProductCategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            ImageUrl = c.ImageUrl,
            CreatedAt = c.CreatedAt
        };
    }
}
