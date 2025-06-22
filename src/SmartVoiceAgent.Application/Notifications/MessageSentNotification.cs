using MediatR;

namespace SmartVoiceAgent.Application.Notifications;

public record MessageSentNotification(string Recipient, string Message) : INotification;
