using MediatR;

namespace SmartVoiceAgent.Application.NotificationHandlers;

public record ApplicationClosedNotification(string ApplicationName) : INotification;
