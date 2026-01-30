using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Message;

namespace SmartVoiceAgent.Tests.Infrastructure.Services
{
    /// <summary>
    /// Unit tests for Message Service implementations
    /// </summary>
    public class MessageServiceTests
    {
        #region EmailMessageService Tests

        [Theory]
        [InlineData("test@example.com", true)]
        [InlineData("user.name@domain.co.uk", true)]
        [InlineData("user+tag@example.com", true)]
        [InlineData("invalid-email", false)]
        [InlineData("@example.com", false)]
        [InlineData("user@", false)]
        [InlineData("", false)]
        [InlineData("not-an-email", false)]
        public void EmailMessageService_CanHandle_Should_Validate_Email_Addresses(string recipient, bool expected)
        {
            // Arrange
            var config = new ConfigurationBuilder().Build();
            var service = new EmailMessageService(config);

            // Act
            var result = service.CanHandle(recipient);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void MessageServiceFactory_GetService_With_Email_Should_Return_EmailService()
        {
            // Arrange
            var config = new ConfigurationBuilder().Build();
            var factory = new MessageServiceFactory(config);

            // Act
            var service = factory.GetService("test@example.com");

            // Assert
            service.Should().NotBeNull();
            service.Should().BeOfType<EmailMessageService>();
        }

        [Fact]
        public void MessageServiceFactory_GetService_With_Invalid_Recipient_Should_Throw()
        {
            // Arrange
            var config = new ConfigurationBuilder().Build();
            var factory = new MessageServiceFactory(config);

            // Act & Assert
            Action act = () => factory.GetService("invalid-recipient-format");
            act.Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void MessageServiceFactory_GetService_With_Empty_Recipient_Should_Throw()
        {
            // Arrange
            var config = new ConfigurationBuilder().Build();
            var factory = new MessageServiceFactory(config);

            // Act & Assert
            Action act = () => factory.GetService("");
            act.Should().Throw<ArgumentException>();
        }

        #endregion

        #region IMessageService Interface Contract Tests

        [Theory]
        [InlineData("user@example.com")]
        [InlineData("test.user@domain.com")]
        public void All_Services_CanHandle_Should_Work_With_Valid_Emails(string email)
        {
            // Arrange
            var config = new ConfigurationBuilder().Build();
            var factory = new MessageServiceFactory(config);
            var service = factory.GetService(email);

            // Act & Assert
            service.CanHandle(email).Should().BeTrue();
        }

        #endregion
    }
}
