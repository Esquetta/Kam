using System.Threading.Channels;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service for submitting text commands from UI to the agent system
/// </summary>
public interface ICommandInputService
{
    /// <summary>
    /// Submits a command to be processed by the agent system
    /// </summary>
    void SubmitCommand(string command);

    /// <summary>
    /// Waits for and reads the next submitted command asynchronously
    /// </summary>
    Task<string> ReadCommandAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a command result is available
    /// </summary>
    event EventHandler<CommandResultEventArgs>? OnResult;

    /// <summary>
    /// Publishes a result from command execution
    /// </summary>
    void PublishResult(string command, string result, bool success = true);
}

public class CommandResultEventArgs : System.EventArgs
{
    public string Command { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool Success { get; set; } = true;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
