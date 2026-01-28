namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service for broadcasting logs from backend agents to the UI
/// </summary>
public interface IUiLogService
{
    /// <summary>
    /// Event raised when a new log entry is available
    /// </summary>
    event EventHandler<UiLogEntry>? OnLogEntry;

    /// <summary>
    /// Sends a log entry to the UI
    /// </summary>
    void Log(string message, LogLevel level = LogLevel.Information, string? source = null);

    /// <summary>
    /// Sends an agent execution update to the UI
    /// </summary>
    void LogAgentUpdate(string agentName, string message, bool isComplete = false);
}

/// <summary>
/// Log entry for UI display
/// </summary>
public class UiLogEntry : System.EventArgs
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; } = LogLevel.Information;
    public string? Source { get; set; }
    public string? AgentName { get; set; }
    public bool IsAgentUpdate { get; set; }
    public bool IsComplete { get; set; }
}

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
