using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Common.Models;
using FloraCore.Domain.Entities;
using FloraCore.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace FloraCore.Tests.Infrastructure.Services;

public class PostCrawlerServiceTests
{
    private readonly Mock<IGenericRepository<Post, Guid>> _mockPostRepository;
    private readonly Mock<UserManager<AppUser>> _mockUserManager;
    private readonly Mock<IOptions<FanpageAiManagerSettings>> _mockSettings;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly FanpageAiManagerSettings _settings;

    public PostCrawlerServiceTests()
    {
        _mockPostRepository = new Mock<IGenericRepository<Post, Guid>>();
        
        var userStoreMock = new Mock<IUserStore<AppUser>>();
        _mockUserManager = new Mock<UserManager<AppUser>>(userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _settings = new FanpageAiManagerSettings
        {
            BaseUrl = "http://localhost:3000/api/external",
            ApiKey = "test_key"
        };
        _mockSettings = new Mock<IOptions<FanpageAiManagerSettings>>();
        _mockSettings.Setup(x => x.Value).Returns(_settings);

        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
    }

    [Fact]
    public async Task CrawlAndSavePostsAsync_ValidResponse_SavesUnapprovedPosts()
    {
        // Arrange
        var categoryId = "test-category";
        var adminUser = new AppUser { Id = Guid.NewGuid(), Email = "admin@floracore.com" };
        _mockUserManager.Setup(x => x.FindByEmailAsync("admin@floracore.com")).ReturnsAsync(adminUser);

        var apiResponse = new
        {
            Success = true,
            Count = 2,
            Data = new[]
            {
                new { Id = "1", Topic = "Topic 1", Content = "Content 1", Status = "published" },
                new { Id = "2", Topic = "Topic 2", Content = "Content 2", Status = "published" }
            }
        };

        var responseJson = JsonSerializer.Serialize(apiResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        _mockPostRepository.Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Post, bool>>>()))
            .ReturnsAsync(false);

        var service = new PostCrawlerService(_httpClient, _mockPostRepository.Object, _mockUserManager.Object, _mockSettings.Object);

        // Act
        var result = await service.CrawlAndSavePostsAsync(categoryId, "topic-123", "published", 20);

        // Assert
        result.Should().Be(2);
        _mockPostRepository.Verify(x => x.AddAsync(It.Is<Post>(p => 
            p.CategoryId == categoryId && 
            p.AuthorId == adminUser.Id && 
            p.IsApproved == false)), Times.Exactly(2));
    }

    [Fact]
    public async Task CrawlAndSavePostsAsync_WithDuplicatePost_DoesNotSaveDuplicates()
    {
        // Arrange
        var categoryId = "test-category";
        var adminUser = new AppUser { Id = Guid.NewGuid(), Email = "admin@floracore.com" };
        _mockUserManager.Setup(x => x.FindByEmailAsync("admin@floracore.com")).ReturnsAsync(adminUser);

        var apiResponse = new
        {
            Success = true,
            Count = 1,
            Data = new[]
            {
                new { Id = "1", Topic = "Topic 1", Content = "Content 1", Status = "published" }
            }
        };

        var responseJson = JsonSerializer.Serialize(apiResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Return true (meaning post already exists)
        _mockPostRepository.Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Post, bool>>>()))
            .ReturnsAsync(true);

        var service = new PostCrawlerService(_httpClient, _mockPostRepository.Object, _mockUserManager.Object, _mockSettings.Object);

        // Act
        var result = await service.CrawlAndSavePostsAsync(categoryId, "topic-123", "published", 20);

        // Assert
        result.Should().Be(0);
        _mockPostRepository.Verify(x => x.AddAsync(It.IsAny<Post>()), Times.Never);
    }
}
