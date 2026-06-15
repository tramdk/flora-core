using FloraCore.Application.Features.WebsiteInfo.Commands;
using FloraCore.Application.Interfaces;
using FloraCore.Application.Common.Interfaces;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using FloraCore.Domain.Entities;

namespace FloraCore.Tests.Application.WebsiteInfo
{
    public class UpdateWebsiteInfoCommandHandlerTests
    {
        [Fact]
        public async Task Handle_ExistingId_UpdatesWebsiteInfo()
        {
            // Arrange
            var mockRepository = new Mock<IWebsiteInfoRepository>();
            var mockResourceManager = new Mock<IResourceManager>();
            var handler = new UpdateWebsiteInfoCommandHandler(mockRepository.Object, mockResourceManager.Object);
            var existingWebsiteInfo = new FloraCore.Domain.Entities.WebsiteInfo { Id = Guid.NewGuid(), Name = "Old Name" };
            mockRepository.Setup(repo => repo.GetByIdAsync(existingWebsiteInfo.Id)).ReturnsAsync(existingWebsiteInfo);
            mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<FloraCore.Domain.Entities.WebsiteInfo>())).Returns(Task.CompletedTask);

            var command = new UpdateWebsiteInfoCommand(
                existingWebsiteInfo.Id,
                "New Name",
                "New Slogan",
                "new@example.com",
                "0987654321",
                "987654321",
                "New Location",
                "New Location"
            );

            // Act
            await handler.Handle(command, CancellationToken.None);

            // Assert
            mockRepository.Verify(repo => repo.GetByIdAsync(existingWebsiteInfo.Id), Times.Once);
            mockRepository.Verify(repo => repo.UpdateAsync(It.Is<FloraCore.Domain.Entities.WebsiteInfo>(w => w.Name == "New Name")), Times.Once);
        }

        [Fact]
        public async Task Handle_NonExistingId_ThrowsArgumentException()
        {
            // Arrange
            var mockRepository = new Mock<IWebsiteInfoRepository>();
            var mockResourceManager = new Mock<IResourceManager>();
            mockResourceManager.Setup(r => r.GetString("WebsiteInfoNotFound")).Returns("WebsiteInfo not found.");
            var handler = new UpdateWebsiteInfoCommandHandler(mockRepository.Object, mockResourceManager.Object);
            Guid nonExistingId = Guid.NewGuid();
            mockRepository.Setup(repo => repo.GetByIdAsync(nonExistingId)).ReturnsAsync((FloraCore.Domain.Entities.WebsiteInfo)null);

            var command = new UpdateWebsiteInfoCommand(
                nonExistingId,
                "New Name",
                "New Slogan",
                "new@example.com",
                "0987654321",
                "987654321",
                "New Location",
                "New Location"
            );

            // Act
            Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>().WithMessage("WebsiteInfo not found.");
            mockRepository.Verify(repo => repo.GetByIdAsync(nonExistingId), Times.Once);
            mockRepository.Verify(repo => repo.UpdateAsync(It.IsAny<FloraCore.Domain.Entities.WebsiteInfo>()), Times.Never);
        }
    }
}
