using System;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Features.Posts.Commands;
using FloraCore.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FloraCore.Tests.Application.Features.Posts.Commands;

public class ApprovePostCommandHandlerTests
{
    private readonly Mock<IGenericRepository<Post, Guid>> _mockPostRepository;
    private readonly ApprovePostCommandHandler _handler;

    public ApprovePostCommandHandlerTests()
    {
        _mockPostRepository = new Mock<IGenericRepository<Post, Guid>>();
        _handler = new ApprovePostCommandHandler(_mockPostRepository.Object);
    }

    [Fact]
    public async Task Handle_Should_ApprovePost_When_PostExists()
    {
        // Arrange
        var postId = Guid.NewGuid();
        var post = new Post { Id = postId, Title = "Test Title", Content = "Test Content", IsApproved = false };
        _mockPostRepository.Setup(x => x.GetByIdAsync(postId)).ReturnsAsync(post);

        var command = new ApprovePostCommand(postId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        post.IsApproved.Should().BeTrue();
        _mockPostRepository.Verify(x => x.UpdateAsync(post), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFalse_When_PostDoesNotExist()
    {
        // Arrange
        var postId = Guid.NewGuid();
        _mockPostRepository.Setup(x => x.GetByIdAsync(postId)).ReturnsAsync((Post?)null);

        var command = new ApprovePostCommand(postId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        _mockPostRepository.Verify(x => x.UpdateAsync(It.IsAny<Post>()), Times.Never);
    }
}
