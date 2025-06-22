using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using SmartVoiceAgent.Application.Notifications;

namespace SmartVoiceAgent.Application.NotificationHandlers
{
    public class WebSearchedNotificationHandler : INotificationHandler<WebSearchedNotification>
    {
        private readonly LoggerServiceBase _logger;

        public WebSearchedNotificationHandler(LoggerServiceBase logger)
        {
            _logger = logger;
        }

        public Task Handle(WebSearchedNotification notification, CancellationToken cancellationToken)
        {
            _logger.Info($"🔍 Web search performed: {notification.Query}");
            return Task.CompletedTask;
        }
    }
}
