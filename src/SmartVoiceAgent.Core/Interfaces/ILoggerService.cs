namespace SmartVoiceAgent.Core.Interfaces
{
    /// <summary>
    /// General-purpose logger interface for application-wide logging.
    /// </summary>
    public interface ILoggerService
    {
        void LogInformation(string message, object? data = null);
        void LogWarning(string message, object? data = null);
        void LogError(string message, Exception ex, object? data = null);
    }
}
