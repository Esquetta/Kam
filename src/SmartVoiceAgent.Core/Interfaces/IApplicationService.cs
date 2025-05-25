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
}
