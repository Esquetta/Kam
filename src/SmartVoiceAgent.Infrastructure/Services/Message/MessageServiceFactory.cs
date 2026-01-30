using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.Message
{
    /// <summary>
    /// Factory for creating message services based on recipient type
    /// </summary>
    public class MessageServiceFactory : IMessageServiceFactory
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailMessageService>? _emailLogger;
        private readonly List<IMessageService> _services;

        public MessageServiceFactory(IConfiguration configuration, ILogger<EmailMessageService>? emailLogger = null)
        {
            _configuration = configuration;
            _emailLogger = emailLogger;
            
            // Initialize all available message services
            _services = new List<IMessageService>
            {
                new EmailMessageService(_configuration, _emailLogger)
                // Add more services here (SMS, Slack, etc.)
            };
        }

        /// <summary>
        /// Gets the appropriate message service for the recipient
        /// </summary>
        public IMessageService GetService(string recipient)
        {
            if (string.IsNullOrWhiteSpace(recipient))
                throw new ArgumentException("Recipient cannot be empty", nameof(recipient));
            
            // Find the first service that can handle this recipient
            var service = _services.FirstOrDefault(s => s.CanHandle(recipient));
            
            if (service == null)
            {
                throw new NotSupportedException($"No message service found for recipient: {recipient}. " +
                    "Supported formats: email addresses (user@example.com)");
            }
            
            return service;
        }
    }
}
