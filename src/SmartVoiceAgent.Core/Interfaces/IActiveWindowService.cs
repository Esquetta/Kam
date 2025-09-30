using SmartVoiceAgent.Core.Dtos;

namespace SmartVoiceAgent.Core.Interfaces;
public interface IActiveWindowService
{
    /// <summary>
    /// Gets information about the currently active window.
    /// </summary>
    /// <returns>Active window information or null if no window is active.</returns>
    Task<ActiveWindowInfo> GetActiveWindowInfoAsync();

    /// <summary>
    /// Gets information about all visible windows.
    /// </summary>
    /// <returns>Collection of window information.</returns>
    Task<IEnumerable<ActiveWindowInfo>> GetAllWindowsAsync();
}