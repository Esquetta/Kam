using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.UiLog;

/// <summary>
/// Fallback implementation that writes to console when UI is not connected
/// </summary>
public class ConsoleUiLogService : IUiLogService
{
    public event EventHandler<UiLogEntry>? OnLogEntry;

    public void Log(string message, LogLevel level = LogLevel.Information, string? source = null)
    {
        var prefix = level switch
        {
            LogLevel.Debug => "[DBG]",
            LogLevel.Information => "[INF]",
            LogLevel.Warning => "[WRN]",
            LogLevel.Error => "[ERR]",
            LogLevel.Critical => "[CRT]",
            _ => "[INF]"
        };

        var sourceStr = string.IsNullOrEmpty(source) ? "" : $"[{source}] ";
        Console.WriteLine($"{prefix} {sourceStr}{message}");
    }

    public void LogAgentUpdate(string agentName, string message, bool isComplete = false)
    {
        var prefix = isComplete ? "✓" : "►";
        Console.WriteLine($"{prefix} [{agentName.ToUpper()}] {message}");
    }
}
