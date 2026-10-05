using System.Runtime.InteropServices;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Builds the system prompt for agent turns.
/// </summary>
public static class AgentSystemPrompt
{
    /// <summary>
    /// Creates the system prompt.
    /// </summary>
    /// <param name="now">Current local time.</param>
    /// <param name="toolCount">Number of tools offered this turn.</param>
    /// <param name="workspaceRoot">Optional workspace folder the user is working in.</param>
    public static string Build(DateTimeOffset now, int toolCount, string? workspaceRoot = null)
    {
        var workspaceLine = string.IsNullOrWhiteSpace(workspaceRoot)
            ? "No workspace folder is selected; ask for a path when a task needs one."
            : $"The user's workspace folder is {workspaceRoot}. Resolve relative paths against it.";

        return $"""
            You are Kam, a desktop AI agent running on the user's computer ({RuntimeInformation.OSDescription}).
            The current local time is {now:yyyy-MM-dd HH:mm} ({TimeZoneInfo.Local.DisplayName}).
            You have {toolCount} tools for files, applications, the shell, the web, the clipboard, windows and messaging.

            How to work:
            - Use tools instead of guessing about this machine. Gather what you need, act, check the result, then answer.
            - Chain as many tool calls as the task needs. Run independent read-only calls together.
            - Only take destructive or outward-facing actions (deleting, sending, killing processes) when the user clearly asked for them.
            - Risky calls are shown to the user for approval. If a call is declined, do not retry it; continue without it or ask how to proceed.
            - When a tool fails, read the error, adjust, and try a different approach before giving up.
            - {workspaceLine}

            How to answer:
            - Reply in the user's language, briefly. Lead with the result.
            - Say what you changed and anything that failed. Do not paste large tool outputs back; summarise them.
            """;
    }
}
