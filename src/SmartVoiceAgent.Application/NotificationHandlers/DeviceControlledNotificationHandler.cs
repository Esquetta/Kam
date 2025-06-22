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
            _logger.Info($"🖥️ Device '{notification.DeviceName}' action: {notification.Action}");
            return Task.CompletedTask;
        }
    }
}
