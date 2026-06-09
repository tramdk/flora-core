using FloraCore.Application.Common.Extensions;
using FloraCore.Application.Common.Helpers;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Common.Models;
using FloraCore.Application.Features.Posts.DTOs;
using FloraCore.Application.Features.Posts.Extensions;
using FloraCore.Domain.Entities;
using MediatR;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace FloraCore.Application.Features.Posts.Queries;

/// <summary>
/// Unified search query that supports multiple approaches:
/// 1. Simple parameters (searchTerm, categoryId, etc.)
/// 2. FilterModel (AG-Grid, MUI DataGrid style)
/// 3. Mixed approach
/// </summary>
public record UnifiedSearchPostsQuery(UnifiedSearchRequest Request) 
    : IRequest<PagedResult<PostDto>>;

public class UnifiedSearchPostsHandler(IGenericRepository<Post, Guid> repository, IMapper mapper) 
    : IRequestHandler<UnifiedSearchPostsQuery, PagedResult<PostDto>>
{
    private readonly IGenericRepository<Post, Guid> _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IMapper _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    
    
    public async Task<PagedResult<PostDto>> Handle(
        UnifiedSearchPostsQuery request, 
        CancellationToken ct)
    {
        var searchRequest = request.Request;
        
        // Build filter expression
        Expression<Func<Post, bool>>? filter = null;
        
        if (searchRequest.IsFilterModelRequest())
        {
            // Use FilterModel approach
            var filterModel = new FilterModel
            {
                Filters = searchRequest.Filters!,
                Sort = searchRequest.Sort ?? new List<SortModel>(),
                Page = searchRequest.GetEffectivePage(),
                PageSize = searchRequest.PageSize
            };
            
            filter = FilterModelParser.ParseFilter<Post>(filterModel);
        }
        else if (searchRequest.IsSimpleSearchRequest())
        {
            // Use simple search approach
            filter = BuildSimpleFilter(searchRequest);
        }

        // If IncludeUnapproved is not set to true, only return approved posts
        if (searchRequest.IncludeUnapproved != true)
        {
            Expression<Func<Post, bool>> approvedFilter = p => p.IsApproved;
            filter = filter == null ? approvedFilter : filter.And(approvedFilter);
        }
        
        // Build query options
        var optionsBuilder = new QueryOptionsBuilder<Post>()
            .WithInclude(p => p.Author!)
            .AsNoTracking();
        
        // Apply filter
        if (filter != null)
        {
            optionsBuilder.WithFilter(filter);
        }
        
        // Apply sorting
        ApplySorting(optionsBuilder, searchRequest);
        
        // Apply pagination
        var skip = searchRequest.GetEffectivePage() * searchRequest.PageSize;
        optionsBuilder.WithPagination(skip, searchRequest.PageSize);
        
        var queryOptions = optionsBuilder.Build();
        
        // Build query and project to DTO using AutoMapper
        var query = _repository.GetQueryable(queryOptions);
        
        var items = await query
            .ProjectTo<PostDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);
            
        var count = await _repository.CountAsync(queryOptions.Filter);

        return new PagedResult<PostDto>(
            items,
            count,
            searchRequest.Page ?? 1,
            searchRequest.PageSize
        );
    }
    
    /// <summary>
    /// Build filter from simple search parameters
    /// </summary>
    private Expression<Func<Post, bool>>? BuildSimpleFilter(UnifiedSearchRequest request)
    {
        Expression<Func<Post, bool>>? filter = null;
        
        // Search term filter
        if (!string.IsNullOrEmpty(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.ToLower();
            Expression<Func<Post, bool>> searchFilter = p => 
                p.Title.ToLower().Contains(searchTerm) || 
                p.Content.ToLower().Contains(searchTerm);
            
            filter = filter == null ? searchFilter : filter.And(searchFilter);
        }
        
        // Category filter
        if (!string.IsNullOrEmpty(request.CategoryId))
        {
            Expression<Func<Post, bool>> categoryFilter = p => 
                p.CategoryId == request.CategoryId;
            
            filter = filter == null ? categoryFilter : filter.And(categoryFilter);
        }
        
        // Rating filter
        if (request.MinRating.HasValue)
        {
            Expression<Func<Post, bool>> ratingFilter = p => 
                p.AverageRating >= request.MinRating.Value;
            
            filter = filter == null ? ratingFilter : filter.And(ratingFilter);
        }
        
        // Date range filter
        if (request.FromDate.HasValue)
        {
            Expression<Func<Post, bool>> fromDateFilter = p => 
                p.CreatedAt >= request.FromDate.Value;
            
            filter = filter == null ? fromDateFilter : filter.And(fromDateFilter);
        }
        
        if (request.ToDate.HasValue)
        {
            Expression<Func<Post, bool>> toDateFilter = p => 
                p.CreatedAt <= request.ToDate.Value;
            
            filter = filter == null ? toDateFilter : filter.And(toDateFilter);
        }
        
        return filter;
    }
    
    /// <summary>
    /// Apply sorting to query options
    /// </summary>
    private void ApplySorting(QueryOptionsBuilder<Post> builder, UnifiedSearchRequest request)
    {
        // Check if using FilterModel sort
        if (request.Sort != null && request.Sort.Any())
        {
            var options = builder.Build();
            FilterModelParser.ApplySorting(options, request.Sort);
            
            if (options.OrderBy != null)
                builder.WithOrderBy(options.OrderBy);
            else if (options.OrderByDescending != null)
                builder.WithOrderByDescending(options.OrderByDescending);
            
            return;
        }
        
        // Use simple sort parameters
        if (!string.IsNullOrEmpty(request.SortBy))
        {
            var sortDescending = request.SortDescending ?? true;
            
            Expression<Func<Post, object>>? sortExpression = request.SortBy.ToLower() switch
            {
                "title" => p => p.Title,
                "rating" => p => p.AverageRating,
                "createdat" => p => p.CreatedAt,
                _ => p => p.CreatedAt
            };
            
            if (sortDescending)
                builder.WithOrderByDescending(sortExpression);
            else
                builder.WithOrderBy(sortExpression);
        }
        else
        {
            // Default sort by CreatedAt descending
            builder.WithOrderByDescending(p => p.CreatedAt);
        }
    }
}
