using FluentAssertions;
using Moq;
using SmartVoiceAgent.Application.Handlers;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Tests.Application.Handlers
{
    public class PlayMusicCommandHandlerTests
    {
        [Fact]
        public async Task HandleAsync_Should_Return_Success()
        {
            // Arrange
            var mockMusicService = new Mock<IMusicService>();
            mockMusicService.Setup(x => x.PlayMusicAsync(It.IsAny<string>()))
                            .Returns(Task.CompletedTask);

            var handler = new PlayMusicCommandHandler();
            var command = new PlayMusicCommand("metallica");

            // Act
            var result = await handler.HandleAsync(command);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Be("Playing metallica.");
        }
    }
}
