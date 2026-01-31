using MediatR;

namespace SmartVoiceAgent.Application.Notifications;

/// <summary>
/// Notification published when a device control action is performed
/// </summary>
public record DeviceControlledNotification(string DeviceName, string Action) : INotification
{
    /// <summary>
    /// Whether the control action was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Result message from the control action
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
