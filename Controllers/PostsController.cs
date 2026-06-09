using FloraCore.Application.Features.Posts.Queries;
using FloraCore.Application.Features.Posts.Commands;
using FloraCore.Application.Features.Posts.DTOs;
using FloraCore.Application.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace FloraCore.Controllers;

/// <summary>
/// Controller for managing blog posts.
/// </summary>
/// <param name="mediator">The mediator instance for handling commands and queries.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class PostsController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Get all posts with basic pagination.
    /// </summary>
    /// <param name="cursor">The cursor for pagination (optional).</param>
    /// <param name="pageSize">The number of items per page (default is 10).</param>
    /// <returns>A list of posts.</returns>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? cursor = null, [FromQuery] int pageSize = 10)
    {
        var result = await _mediator.Send(new GetPostsQuery(cursor, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Unified search endpoint (GET) - supports multiple search approaches.
    /// </summary>
    /// <param name="searchTerm">The term to search for.</param>
    /// <param name="categoryId">The category ID to filter by.</param>
    /// <param name="minRating">The minimum rating to filter by.</param>
    /// <param name="fromDate">The start date for filtering.</param>
    /// <param name="toDate">The end date for filtering.</param>
    /// <param name="sortBy">The field to sort by.</param>
    /// <param name="sortDescending">Whether to sort in descending order.</param>
    /// <param name="page">The page number for pagination.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <param name="includeUnapproved">Whether to include unapproved posts (admin only).</param>
    /// <returns>Search results.</returns>
    [HttpGet("search")]
    [HttpGet("unified")] // For backward compatibility
    [AllowAnonymous]
    public async Task<IActionResult> SearchGet(
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? categoryId = null,
        [FromQuery] double? minRating = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool? sortDescending = null,
        [FromQuery] int? page = null,
        [FromQuery] int pageSize = 10,
        [FromQuery] bool? includeUnapproved = null)
    {
        var isAdmin = User.Identity?.IsAuthenticated == true && User.IsInRole("Admin");
        var request = new UnifiedSearchRequest
        {
            SearchTerm = searchTerm,
            CategoryId = categoryId,
            MinRating = minRating,
            FromDate = fromDate,
            ToDate = toDate,
            SortBy = sortBy,
            SortDescending = sortDescending,
            Page = page,
            PageSize = pageSize > 0 ? pageSize : 10,
            IncludeUnapproved = isAdmin ? includeUnapproved : null
        };

        var query = new UnifiedSearchPostsQuery(request);
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Unified search endpoint (POST) - supports complex filter models.
    /// </summary>
    /// <param name="request">The search request containing filters and search terms.</param>
    /// <returns>Search results.</returns>
    [HttpPost("search")]
    [HttpPost("unified")] // For backward compatibility
    [AllowAnonymous]
    public async Task<IActionResult> SearchPost([FromBody] UnifiedSearchRequest request)
    {
        if (request == null)
            return BadRequest();

        var isAdmin = User.Identity?.IsAuthenticated == true && User.IsInRole("Admin");
        if (!isAdmin)
        {
            request.IncludeUnapproved = null; // Enforce security filter for non-admins
        }

        var query = new UnifiedSearchPostsQuery(request);
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get a single post by its ID.
    /// </summary>
    /// <param name="id">The ID of the post.</param>
    /// <returns>The post details or NotFound if not found.</returns>
    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPost(Guid id)
    {
        var query = new GetPostDetailQuery(id);
        var response = await _mediator.Send(query);
        if (response == null) return NotFound();
        return Ok(response);
    }

    /// <summary>
    /// Create a new post.
    /// </summary>
    /// <param name="command">The command containing post details.</param>
    /// <returns>The ID of the newly created post.</returns>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreatePostCommand command)
    {
        var id = await _mediator.Send(command);
        return Ok(id);
    }

    /// <summary>
    /// Rate a post.
    /// </summary>
    /// <param name="id">The ID of the post to rate.</param>
    /// <param name="score">The rating score.</param>
    /// <returns>Ok if successful, NotFound if the post does not exist.</returns>
    [HttpPost("{id}/rate")]
    [Authorize]
    public async Task<IActionResult> Rate(Guid id, [FromBody] int score)
    {
        var result = await _mediator.Send(new RatePostCommand(id, score));
        return result ? Ok() : NotFound();
    }
    
    /// <summary>
    /// Update an existing post.
    /// </summary>
    /// <param name="id">The ID of the post to update.</param>
    /// <param name="command">The command containing updated post details.</param>
    /// <returns>NoContent if successful, BadRequest if ID mismatch, or NotFound if the post does not exist.</returns>
    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePostCommand command)
    {
        if (id != command.Id) return BadRequest("ID mismatch");
        var result = await _mediator.Send(command);
        return result ? NoContent() : NotFound();
    }

    /// <summary>
    /// Delete a post.
    /// </summary>
    /// <param name="id">The ID of the post to delete.</param>
    /// <returns>NoContent if successful, or NotFound if the post does not exist.</returns>
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeletePostCommand(id));
        return result ? NoContent() : NotFound();
    }

    /// <summary>
    /// Crawls blog posts from Fanpage-AI-Manager.
    /// </summary>
    [HttpPost("crawl-external")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TriggerCrawl(
        [FromQuery] string categoryId,
        [FromQuery] string? topicId = null,
        [FromQuery] string? status = "published",
        [FromQuery] int limit = 20,
        [FromServices] IPostCrawlerService crawlerService = null!)
    {
        if (string.IsNullOrEmpty(categoryId))
        {
            return BadRequest("CategoryId is required to categorize the crawled posts.");
        }

        var count = await crawlerService.CrawlAndSavePostsAsync(categoryId, topicId, status, limit);
        return Ok(new { Message = $"Cào thành công và đã thêm mới {count} bài viết ở trạng thái chờ duyệt." });
    }

    /// <summary>
    /// Approves an unapproved blog post.
    /// </summary>
    [HttpPost("{id}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(Guid id)
    {
        var result = await _mediator.Send(new ApprovePostCommand(id));
        return result ? Ok(new { Message = "Post approved successfully." }) : NotFound();
    }
}
