using FloraCore.Domain.Entities;
using FloraCore.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FloraCore.Application.Features.ProductCategories.DTOs;

using FloraCore.Application.Features.ProductCategories.DTOs;

namespace FloraCore.Application.Features.ProductCategories.Queries;

public record GetProductCategoriesQuery : IRequest<List<ProductCategoryDto>>;

public class GetProductCategoriesQueryHandler : IRequestHandler<GetProductCategoriesQuery, List<ProductCategoryDto>>
{
    private readonly IGenericRepository<ProductCategory, Guid> _repository;

    public GetProductCategoriesQueryHandler(IGenericRepository<ProductCategory, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<List<ProductCategoryDto>> Handle(GetProductCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await _repository.GetQueryable()
            .OrderBy(c => c.Name)
            .Select(c => new ProductCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                ImageUrl = c.ImageUrl,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return categories;
    }
}
