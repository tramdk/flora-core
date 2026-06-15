using FloraCore.Domain.Entities;
using FloraCore.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

using FloraCore.Application.Features.PostCategories.DTOs;

using FloraCore.Application.Features.PostCategories.DTOs;

namespace FloraCore.Application.Features.PostCategories.Queries;

public record GetPostCategoryByIdQuery(string Id) : IRequest<PostCategoryDto?>
{
    // ThrowIfNull
}

public class GetPostCategoryByIdQueryHandler(IGenericRepository<PostCategory, string> repository) : IRequestHandler<GetPostCategoryByIdQuery, PostCategoryDto?>
{
    private readonly IGenericRepository<PostCategory, string> _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<PostCategoryDto?> Handle(GetPostCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        var category = await _repository.GetQueryable()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (category == null) return null;

        return new PostCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }
}
