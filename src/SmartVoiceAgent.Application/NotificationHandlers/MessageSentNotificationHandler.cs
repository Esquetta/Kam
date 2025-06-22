using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using SmartVoiceAgent.Application.Notifications;

namespace SmartVoiceAgent.Application.NotificationHandlers
{
    public class MessageSentNotificationHandler : INotificationHandler<MessageSentNotification>
    {
        private readonly LoggerServiceBase _logger;

        public MessageSentNotificationHandler(LoggerServiceBase logger)
        {
            _logger = logger;
        }

        public Task Handle(MessageSentNotification notification, CancellationToken cancellationToken)
        {
            _logger.Info($"📩 Message sent to {notification.Recipient}: {notification.Message}");
            return Task.CompletedTask;
        }
    }
}
