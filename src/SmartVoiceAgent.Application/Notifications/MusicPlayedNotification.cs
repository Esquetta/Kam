using MediatR;

namespace SmartVoiceAgent.Application.Notifications;

/// <summary>
/// Notification published when music is played.
/// </summary>
public record MusicPlayedNotification(string SongName) : INotification;
