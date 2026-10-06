namespace SmartVoiceAgent.Core.Models.Agents;

/// <summary>
/// A saved agent thread as listed in the chat sidebar.
/// </summary>
public sealed record AgentSessionSummary(
    string Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string? WorkspaceRoot);
