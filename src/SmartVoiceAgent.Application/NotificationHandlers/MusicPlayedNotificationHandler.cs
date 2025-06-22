using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using SmartVoiceAgent.Application.Notifications;

namespace SmartVoiceAgent.Application.NotificationHandlers
{
    public class MusicPlayedNotificationHandler : INotificationHandler<MusicPlayedNotification>
    {
        private readonly LoggerServiceBase _logger;

        public MusicPlayedNotificationHandler(LoggerServiceBase logger)
        {
            _logger = logger;
        }

        public Task Handle(MusicPlayedNotification notification, CancellationToken cancellationToken)
        {
            _logger.Info($"🎵 Music played: {notification.SongName}");
            return Task.CompletedTask;
        }
    }
}
