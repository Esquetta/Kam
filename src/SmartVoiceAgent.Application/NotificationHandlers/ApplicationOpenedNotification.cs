using MediatR;

namespace SmartVoiceAgent.Application.NotificationHandlers;

public record ApplicationOpenedNotification(string ApplicationName) : INotification;


