using SmartVoiceAgent.Core.Dtos;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Interface for scanning installed applications.
/// </summary>
public interface IApplicationScanner
{
    /// <summary>
    /// Lists all installed applications asynchronously.
    /// </summary>
    /// <returns>List of application information.</returns>
    Task<IEnumerable<AppInfoDTO>> GetInstalledApplicationsAsync();
}
