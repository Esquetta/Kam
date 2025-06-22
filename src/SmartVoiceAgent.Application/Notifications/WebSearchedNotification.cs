using MediatR;

namespace SmartVoiceAgent.Application.Notifications;

public record WebSearchedNotification(string Query) : INotification;

