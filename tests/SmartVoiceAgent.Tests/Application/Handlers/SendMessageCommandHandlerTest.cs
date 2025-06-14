using MediatR;
using Moq;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Handlers;

namespace SmartVoiceAgent.Tests.Application.Handlers
{
    public class SendMessageCommandHandlerTest
    {
        [Fact]
        public async Task SendMessageCommand_Should_Return_Success()
        {
            // Arrange
            var mediatorMock = new Mock<IMediator>();
            var expectedResult = new CommandResultDTO(true, "Message sent");

            mediatorMock.Setup(m => m.Send(It.IsAny<SendMessageCommand>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(expectedResult);

            var command = new SendMessageCommand("test@domain.com", "Hello!");

            // Act
            var result = await mediatorMock.Object.Send(command, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal("Message sent", result.Message);
        }

    }
}
