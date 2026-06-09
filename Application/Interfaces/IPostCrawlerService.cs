using System.Threading.Tasks;

namespace FloraCore.Application.Interfaces;

/// <summary>
/// Service interface for crawling posts from external systems.
/// </summary>
public interface IPostCrawlerService
{
    /// <summary>
    /// Crawls posts from Fanpage-AI-Manager and saves them in the database.
    /// </summary>
    /// <param name="categoryId">The target category ID in FloraCore where crawled posts will be saved.</param>
    /// <param name="topicId">The topic ID in Fanpage-AI-Manager to filter by.</param>
    /// <param name="status">The post status to filter by.</param>
    /// <param name="limit">The maximum number of posts to fetch.</param>
    /// <returns>The number of new posts saved.</returns>
    Task<int> CrawlAndSavePostsAsync(string categoryId, string? topicId = null, string? status = "published", int limit = 20);
}
