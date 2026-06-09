using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Common.Models;
using FloraCore.Application.Interfaces;
using FloraCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FloraCore.Infrastructure.Services;

/// <summary>
/// Service to crawl posts from Fanpage AI Manager and import them into Flora Core.
/// </summary>
public class PostCrawlerService(
    HttpClient httpClient,
    IGenericRepository<Post, Guid> postRepository,
    UserManager<AppUser> userManager,
    IOptions<FanpageAiManagerSettings> settings) : IPostCrawlerService
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IGenericRepository<Post, Guid> _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
    private readonly UserManager<AppUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly FanpageAiManagerSettings _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public async Task<int> CrawlAndSavePostsAsync(string categoryId, string? topicId = null, string? status = "published", int limit = 20)
    {
        // 1. Find Admin user to set as post author
        var adminUser = await _userManager.FindByEmailAsync("admin@floracore.com");
        var authorId = adminUser?.Id ?? Guid.Empty;

        // 2. Build target URL with parameters
        var url = $"{_settings.BaseUrl}/posts?limit={limit}";
        if (!string.IsNullOrEmpty(topicId))
        {
            url += $"&topicId={Uri.EscapeDataString(topicId)}";
        }
        if (!string.IsNullOrEmpty(status))
        {
            url += $"&status={Uri.EscapeDataString(status)}";
        }

        // 3. Prepare HTTP Request with API Key header
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", _settings.ApiKey);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonString = await response.Content.ReadAsStringAsync();
        
        var options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var crawlResult = JsonSerializer.Deserialize<CrawlResponse>(jsonString, options);

        if (crawlResult?.Data == null || !crawlResult.Success)
        {
            return 0;
        }

        int savedCount = 0;
        foreach (var externalPost in crawlResult.Data)
        {
            // Avoid duplicates: check by title or content
            var exists = await _postRepository.AnyAsync(p => p.Title == externalPost.Topic || p.Content == externalPost.Content);
            if (exists)
            {
                continue;
            }

            var newPost = new Post
            {
                Id = Guid.NewGuid(),
                Title = externalPost.Topic,
                Content = externalPost.Content,
                CategoryId = categoryId,
                CreatedAt = DateTime.UtcNow,
                AuthorId = authorId,
                IsApproved = false // All crawled posts are pending approval by default
            };

            await _postRepository.AddAsync(newPost);
            savedCount++;
        }

        return savedCount;
    }

    private class CrawlResponse
    {
        public bool Success { get; set; }
        public int Count { get; set; }
        public List<ExternalPostDto>? Data { get; set; }
    }

    private class ExternalPostDto
    {
        public string Id { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
