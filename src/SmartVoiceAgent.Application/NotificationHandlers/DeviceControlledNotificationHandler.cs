using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using SmartVoiceAgent.Application.Notifications;

namespace SmartVoiceAgent.Application.NotificationHandlers
{
    public class DeviceControlledNotificationHandler : INotificationHandler<DeviceControlledNotification>
    {
        private readonly LoggerServiceBase _logger;

        public DeviceControlledNotificationHandler(LoggerServiceBase logger)
        {
            _logger = logger;
        }

        public Task Handle(DeviceControlledNotification notification, CancellationToken cancellationToken)
        {
            var statusIcon = notification.Success ? "✅" : "❌";
            var message = string.IsNullOrEmpty(notification.Message) 
                ? $"{notification.Action} on {notification.DeviceName}" 
                : notification.Message;
            
            _logger.Info($"{statusIcon} Device Control: {message}");
            return Task.CompletedTask;
        }
    }
}
