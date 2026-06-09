using FluentAssertions;
using Moq;
using Xunit;
using FloraCore.Application.Common.Behaviors;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Domain.Exceptions;

namespace FloraCore.Tests.Application.Common.Behaviors;

public class AuthorizationBehaviorTests
{
    // Arrange
    private readonly Mock<ICurrentUserService> _mockCurrentUserService;
    private readonly Mock<IGenericRepository<Post, Guid>> _mockPostRepository;
    private readonly Mock<IResourceManager> _mockResourceManager;
    private readonly RequestHandlerDelegate<Unit> _mockNext;

    public AuthorizationBehaviorTests()
    {
        _mockCurrentUserService = new Mock<ICurrentUserService>();
        _mockPostRepository = new Mock<IGenericRepository<Post, Guid>>();
        _mockResourceManager = new Mock<IResourceManager>();
        _mockNext = () => Task.FromResult(Unit.Value);
    }

    [Fact]
    public void Constructor_WithNullCurrentUserService_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => _ = new AuthorizationBehavior<TestRequest, Unit>(null, _mockPostRepository.Object, _mockResourceManager.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("currentUserService");
    }

    [Fact]
    public void Constructor_WithNullPostRepository_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => _ = new AuthorizationBehavior<TestRequest, Unit>(_mockCurrentUserService.Object, null, _mockResourceManager.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("postRepository");
    }

    [Fact]
    public void Constructor_WithNullResourceManager_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => _ = new AuthorizationBehavior<TestRequest, Unit>(_mockCurrentUserService.Object, _mockPostRepository.Object, null);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resourceManager");
    }

    // Dummy request for testing purposes
    public class TestRequest : IRequest<Unit>, IPostOwnershipRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    public interface IPostOwnershipRequest : IOwnershipRequest { }
}
