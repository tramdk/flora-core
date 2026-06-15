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
    public class DeleteWebsiteInfoCommandHandlerTests
    {
        [Fact]
        public async Task Handle_ExistingId_DeletesWebsiteInfo()
        {
            // Arrange
            var mockRepository = new Mock<IWebsiteInfoRepository>();
            var mockResourceManager = new Mock<IResourceManager>();
            var handler = new DeleteWebsiteInfoCommandHandler(mockRepository.Object, mockResourceManager.Object);
            var existingWebsiteInfo = new FloraCore.Domain.Entities.WebsiteInfo { Id = Guid.NewGuid() };
            mockRepository.Setup(repo => repo.GetByIdAsync(existingWebsiteInfo.Id)).ReturnsAsync(existingWebsiteInfo);
            mockRepository.Setup(repo => repo.DeleteAsync(existingWebsiteInfo.Id)).Returns(Task.CompletedTask);

            // Act
            await handler.Handle(new DeleteWebsiteInfoCommand(existingWebsiteInfo.Id), CancellationToken.None);

            // Assert
            mockRepository.Verify(repo => repo.GetByIdAsync(existingWebsiteInfo.Id), Times.Once);
            mockRepository.Verify(repo => repo.DeleteAsync(existingWebsiteInfo.Id), Times.Once);
        }

        [Fact]
        public async Task Handle_NonExistingId_ThrowsArgumentException()
        {
            // Arrange
            var mockRepository = new Mock<IWebsiteInfoRepository>();
            var mockResourceManager = new Mock<IResourceManager>();
            mockResourceManager.Setup(r => r.GetString("WebsiteInfoNotFound")).Returns("WebsiteInfo not found.");
            var handler = new DeleteWebsiteInfoCommandHandler(mockRepository.Object, mockResourceManager.Object);
            Guid nonExistingId = Guid.NewGuid();
            mockRepository.Setup(repo => repo.GetByIdAsync(nonExistingId)).ReturnsAsync((FloraCore.Domain.Entities.WebsiteInfo)null);

            // Act
            Func<Task> act = async () => await handler.Handle(new DeleteWebsiteInfoCommand(nonExistingId), CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>().WithMessage("WebsiteInfo not found.");
            mockRepository.Verify(repo => repo.GetByIdAsync(nonExistingId), Times.Once);
            mockRepository.Verify(repo => repo.DeleteAsync(nonExistingId), Times.Never);
        }
    }
}
