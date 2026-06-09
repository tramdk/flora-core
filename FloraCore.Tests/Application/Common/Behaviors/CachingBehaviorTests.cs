using FluentAssertions;
using Moq;
using Xunit;
using FloraCore.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using FloraCore.Application.Common.Attributes;

namespace FloraCore.Tests.Application.Common.Behaviors;

public class CachingBehaviorTests
{
    private readonly Mock<HybridCache> _mockCache;
    private readonly Mock<ILogger<CachingBehavior<TestRequest, Unit>>> _mockLogger;
    private readonly RequestHandlerDelegate<Unit> _mockNext;

    public CachingBehaviorTests()
    {
        _mockCache = new Mock<HybridCache>();
        _mockLogger = new Mock<ILogger<CachingBehavior<TestRequest, Unit>>>();
        _mockNext = () => Task.FromResult(Unit.Value);
    }

    [Fact]
    public void Constructor_WithNullHybridCache_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => _ = new CachingBehavior<TestRequest, Unit>(null, _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cache");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => _ = new CachingBehavior<TestRequest, Unit>(_mockCache.Object, null);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // Dummy request for testing purposes
    public class TestRequest : IRequest<Unit> { }

    [Cacheable(ExpirationMinutes = 5)]
    public class CacheableTestRequest : IRequest<Unit> { }
}
