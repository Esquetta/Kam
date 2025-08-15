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


    /// <summary>
    /// Searches for a specific application by name.
    /// </summary>
    /// <param name="appName">The application name to search for.</param>
    /// <returns>Application installation information if found.</returns>
    Task<ApplicationInstallInfo> FindApplicationAsync(string appName);

    /// <summary>
    /// Gets the executable path for a specific application.
    /// </summary>
    /// <param name="appName">The application name.</param>
    /// <returns>The executable path if found, null otherwise.</returns>
    Task<string> GetApplicationPathAsync(string appName);
}

public interface IApplicationScannerServiceFactory
{
    IApplicationScanner Create();
}
