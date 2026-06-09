using FloraCore.Application.Common.Models;

namespace FloraCore.Application.Features.Posts.DTOs;

/// <summary>
/// Unified search request that supports multiple approaches:
/// 1. Simple parameters (searchTerm, categoryId, etc.)
/// 2. FilterModel (AG-Grid, MUI DataGrid style)
/// 3. Mixed approach
/// </summary>
public class UnifiedSearchRequest
{
    // ========== Simple Search Parameters ==========
    
    /// <summary>
    /// Search term to match in title or content
    /// </summary>
    public string? SearchTerm { get; set; }
     
    /// <summary>
    /// Filter by category ID
    /// </summary>
    public string? CategoryId { get; set; }
    
    /// <summary>
    /// Minimum rating (0-5)
    /// </summary>
    public double? MinRating { get; set; }
    
    /// <summary>
    /// Filter posts created from this date
    /// </summary>
    public DateTime? FromDate { get; set; }
    
    /// <summary>
    /// Filter posts created to this date
    /// </summary>
    public DateTime? ToDate { get; set; }
    
    /// <summary>
    /// Sort field: Title, Rating, CreatedAt
    /// </summary>
    public string? SortBy { get; set; }
    
    /// <summary>
    /// Sort direction: true for descending, false for ascending
    /// </summary>
    public bool? SortDescending { get; set; }
    
    // ========== Advanced FilterModel ==========
    
    /// <summary>
    /// Advanced filter model (AG-Grid, MUI DataGrid style)
    /// If provided, this takes precedence over simple parameters
    /// </summary>
    public Dictionary<string, FilterCondition>? Filters { get; set; }
    
    /// <summary>
    /// Sort model (AG-Grid, MUI DataGrid style)
    /// If provided, this takes precedence over SortBy/SortDescending
    /// </summary>
    public List<SortModel>? Sort { get; set; }
    
    // ========== Pagination ==========
    
    /// <summary>
    /// Page number (0-based for FilterModel, 1-based for simple search)
    /// </summary>
    public int? Page { get; set; }
    
    /// <summary>
    /// Whether to include unapproved posts in results.
    /// </summary>
    public bool? IncludeUnapproved { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; } = 10;
}
