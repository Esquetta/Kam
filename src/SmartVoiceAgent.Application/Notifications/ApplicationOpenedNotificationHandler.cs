using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;

namespace SmartVoiceAgent.Application.NotificationHandlers
{
    public class ApplicationOpenedNotificationHandler : INotificationHandler<ApplicationOpenedNotification>
    {
        private readonly LoggerServiceBase _logger;

        public ApplicationOpenedNotificationHandler(LoggerServiceBase logger)
        {
            _logger = logger;
        }

        public Task Handle(ApplicationOpenedNotification notification, CancellationToken cancellationToken)
        {
            _logger.Info($"📱 Application opened: {notification.ApplicationName}");
            return Task.CompletedTask;
        }
    }
}
