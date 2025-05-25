namespace SmartVoiceAgent.Core.Entities
{
    /// <summary>
    /// Represents information about an application on the system.
    /// </summary>
    public record AppInfo(string Name, string Path, bool IsRunning);
}
