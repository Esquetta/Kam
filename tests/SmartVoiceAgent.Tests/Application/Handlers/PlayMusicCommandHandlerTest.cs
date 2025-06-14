using FluentAssertions;
using MediatR;
using Moq;

namespace SmartVoiceAgent.Tests.Application.Commands
{
    public class PlayMusicCommandTests
    {
        [Fact]
        public async Task Send_PlayMusicCommand_Should_Return_Success()
        {
            // Arrange
            var mediatorMock = new Mock<IMediator>();
            var expectedResult = new CommandResultDTO(true, "Playing metallica.");

            mediatorMock.Setup(m => m.Send(It.IsAny<PlayMusicCommand>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(expectedResult);

            var command = new PlayMusicCommand("metallica");

            // Act
            var result = await mediatorMock.Object.Send(command);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Message.Should().Be("Playing metallica.");
        }
    }
}
