using SmartVoiceAgent.Core.Dtos;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for managing applications (open, close, status).
/// </summary>
public interface IApplicationService
{
    /// <summary>
    /// Opens an application by its name asynchronously.
    /// </summary>
    /// <param name="appName">The name of the application to open.</param>
    Task OpenApplicationAsync(string appName);

    /// <summary>
    /// Gets the status of the specified application asynchronously.
    /// </summary>
    /// <param name="appName">The application name.</param>
    /// <returns>The application status.</returns>
    Task<Enums.AppStatus> GetApplicationStatusAsync(string appName);

    Task CloseApplicationAsync(string appName);
    Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync();

    // <summary>
    /// Checks if an application is installed and returns its executable path if found.
    /// </summary>
    /// <param name="appName">The name of the application to search for.</param>
    /// <returns>ApplicationInstallInfo containing installation status and path.</returns>
    Task<ApplicationInstallInfo> CheckApplicationInstallationAsync(string appName);

    /// <summary>
    /// Gets the executable path of an installed application.
    /// </summary>
    /// <param name="appName">The name of the application.</param>
    /// <returns>The executable path if found, null otherwise.</returns>
    Task<string> GetApplicationExecutablePathAsync(string appName);

}
