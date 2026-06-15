using FloraCore.Domain.Entities;
using FloraCore.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FloraCore.Application.Features.PostCategories.DTOs;

using FloraCore.Application.Features.PostCategories.DTOs;

namespace FloraCore.Application.Features.PostCategories.Queries;

public record GetPostCategoriesQuery : IRequest<List<PostCategoryDto>>;

public class GetPostCategoriesQueryHandler : IRequestHandler<GetPostCategoriesQuery, List<PostCategoryDto>>
{
    private readonly IGenericRepository<PostCategory, string> _repository;

    public GetPostCategoriesQueryHandler(IGenericRepository<PostCategory, string> repository)
    {
        _repository = repository;
    }

    public async Task<List<PostCategoryDto>> Handle(GetPostCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await _repository.GetQueryable()
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new PostCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return categories;
    }
}
