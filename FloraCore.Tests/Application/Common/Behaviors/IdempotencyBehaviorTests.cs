using FluentAssertions;
using Moq;
using Xunit;
using FloraCore.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Interfaces;

namespace FloraCore.Tests.Application.Common.Behaviors;

public class IdempotencyBehaviorTests
{
    private readonly Mock<IDistributedCache> _mockCache;
    private readonly Mock<IResourceManager> _mockResourceManager;
    private readonly RequestHandlerDelegate<Unit> _mockNext;

    public IdempotencyBehaviorTests()
    {
        _mockCache = new Mock<IDistributedCache>();
        _mockResourceManager = new Mock<IResourceManager>();
        _mockNext = () => Task.FromResult(Unit.Value);
    }

    [Fact]
    public void Constructor_WithNullDistributedCache_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new IdempotencyBehavior<TestCommand, Unit>(null, _mockResourceManager.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cache");
    }

    [Fact]
    public void Constructor_WithNullResourceManager_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new IdempotencyBehavior<TestCommand, Unit>(_mockCache.Object, null);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resourceManager");
    }

    // Dummy command for testing purposes
    public class TestCommand : IRequest<Unit>, IIdempotentCommand
    {
        public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString();
    }
}
