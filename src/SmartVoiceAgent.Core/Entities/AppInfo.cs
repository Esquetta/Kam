namespace SmartVoiceAgent.Core.Entities;

/// <summary>
/// Represents application information detected on the system.
/// </summary>
/// <param name="Name">Application name.</param>
/// <param name="Path">File path of the application executable.</param>
/// <param name="IsRunning">Indicates whether the application is currently running.</param>
public record AppInfo(string Name, string Path, bool IsRunning);
