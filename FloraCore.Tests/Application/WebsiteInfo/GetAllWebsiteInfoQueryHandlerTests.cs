using Xunit;
using FloraCore.Application.Features.WebsiteInfo.Queries;
using FloraCore.Application.Interfaces;
using FloraCore.Application.Common.Models;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FloraCore.Domain.Entities;

namespace FloraCore.Tests.Application.WebsiteInfo
{
    public class GetAllWebsiteInfoQueryHandlerTests
    {
        [Fact]
        public async Task Handle_ValidQuery_ReturnsAllWebsiteInfo()
        {
            // Arrange
            var mockRepo = new Mock<IWebsiteInfoRepository>();
            var handler = new GetAllWebsiteInfoQueryHandler(mockRepo.Object);
            var expectedWebsiteInfos = new List<FloraCore.Domain.Entities.WebsiteInfo>
            {
                new FloraCore.Domain.Entities.WebsiteInfo { Id = Guid.NewGuid(), Name = "Test Name 1" },
                new FloraCore.Domain.Entities.WebsiteInfo { Id = Guid.NewGuid(), Name = "Test Name 2" }
            };
            mockRepo.Setup(repo => repo.GetWithOptionsAsync(It.IsAny<QueryOptions<FloraCore.Domain.Entities.WebsiteInfo>>()))
                .ReturnsAsync(expectedWebsiteInfos as IEnumerable<FloraCore.Domain.Entities.WebsiteInfo>);

            // Act
            var result = await handler.Handle(new GetAllWebsiteInfoQuery(), CancellationToken.None);

            // Assert
            result.Should().BeEquivalentTo(expectedWebsiteInfos);
            mockRepo.Verify(repo => repo.GetWithOptionsAsync(It.IsAny<QueryOptions<FloraCore.Domain.Entities.WebsiteInfo>>()), Times.Once);
        }
    }
}
