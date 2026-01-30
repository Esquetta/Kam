using FluentAssertions;
using MediatR;
using Moq;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Handlers;
using SmartVoiceAgent.Application.Notifications;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Tests.Application.Handlers
{
    /// <summary>
    /// Unit tests for SendMessageCommandHandler
    /// </summary>
    public class SendMessageCommandHandlerTests
    {
        private readonly Mock<IMessageServiceFactory> _messageServiceFactoryMock;
        private readonly Mock<IMessageService> _messageServiceMock;
        private readonly Mock<IMediator> _mediatorMock;
        private readonly SendMessageCommandHandler _handler;

        public SendMessageCommandHandlerTests()
        {
            _messageServiceFactoryMock = new Mock<IMessageServiceFactory>();
            _messageServiceMock = new Mock<IMessageService>();
            _mediatorMock = new Mock<IMediator>();
            _handler = new SendMessageCommandHandler(_messageServiceFactoryMock.Object, _mediatorMock.Object);
        }

        [Fact]
        public async Task Handle_ValidEmail_Should_SendMessage_And_Return_Success()
        {
            // Arrange
            var recipient = "test@example.com";
            var message = "Hello, this is a test message";
            var command = new SendMessageCommand(recipient, message);

            _messageServiceFactoryMock
                .Setup(f => f.GetService(recipient))
                .Returns(_messageServiceMock.Object);

            _messageServiceMock
                .Setup(s => s.SendMessageAsync(recipient, message, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            _mediatorMock
                .Setup(m => m.Publish(It.IsAny<MessageSentNotification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Message.Should().Contain(recipient);

            _messageServiceMock.Verify(
                s => s.SendMessageAsync(recipient, message, null, It.IsAny<CancellationToken>()),
                Times.Once);

            _mediatorMock.Verify(
                m => m.Publish(
                    It.Is<MessageSentNotification>(n => n.Recipient == recipient && n.Message == message),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_MessageService_Returns_False_Should_Return_Failure()
        {
            // Arrange
            var recipient = "test@example.com";
            var message = "Hello";
            var command = new SendMessageCommand(recipient, message);

            _messageServiceFactoryMock
                .Setup(f => f.GetService(recipient))
                .Returns(_messageServiceMock.Object);

            _messageServiceMock
                .Setup(s => s.SendMessageAsync(recipient, message, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Failed to send");

            _mediatorMock.Verify(
                m => m.Publish(It.IsAny<MessageSentNotification>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_Unsupported_Recipient_Should_Return_Failure()
        {
            // Arrange
            var recipient = "invalid-recipient-format";
            var message = "Hello";
            var command = new SendMessageCommand(recipient, message);

            _messageServiceFactoryMock
                .Setup(f => f.GetService(recipient))
                .Throws(new NotSupportedException("No message service found"));

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("No message service found");
        }

        [Fact]
        public async Task Handle_Service_Throws_Exception_Should_Return_Failure()
        {
            // Arrange
            var recipient = "test@example.com";
            var message = "Hello";
            var command = new SendMessageCommand(recipient, message);

            _messageServiceFactoryMock
                .Setup(f => f.GetService(recipient))
                .Throws(new Exception("Service error"));

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Error sending message");
        }

        [Fact]
        public void SendMessageCommand_Should_Implement_Caching_Properties()
        {
            // Arrange
            var command = new SendMessageCommand("test@example.com", "Hello");

            // Assert
            command.CacheKey.Should().Be("SendMessage-test@example.com");
            command.CacheGroupKey.Should().BeNull();
            command.BypassCache.Should().BeFalse();
            command.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(5));
        }

        [Fact]
        public void SendMessageCommand_Should_Implement_Performance_Properties()
        {
            // Arrange
            var command = new SendMessageCommand("test@example.com", "Hello");

            // Assert
            command.EnablePerformanceLogging.Should().BeTrue();
            command.Interval.Should().Be(1);
        }

        [Theory]
        [InlineData("user1@example.com")]
        [InlineData("user.name@domain.co.uk")]
        [InlineData("user+tag@example.com")]
        public async Task Handle_Various_Email_Formats_Should_Work(string email)
        {
            // Arrange
            var message = "Test message";
            var command = new SendMessageCommand(email, message);

            _messageServiceFactoryMock
                .Setup(f => f.GetService(email))
                .Returns(_messageServiceMock.Object);

            _messageServiceMock
                .Setup(s => s.SendMessageAsync(email, message, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
        }
    }
}
