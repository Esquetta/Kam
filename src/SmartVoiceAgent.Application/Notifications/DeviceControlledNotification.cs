using MediatR;

namespace SmartVoiceAgent.Application.Notifications;
public record DeviceControlledNotification(string DeviceName, string Action) : INotification;
