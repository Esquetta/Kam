namespace SmartVoiceAgent.Core.Models.Agents;

/// <summary>
/// A saved agent thread as listed in the chat sidebar.
/// </summary>
/// <param name="Id">The session id.</param>
/// <param name="Title">The title the user gave the thread, or one taken from its first message.</param>
/// <param name="UpdatedAt">When the thread last changed.</param>
/// <param name="MessageCount">How many messages the thread holds.</param>
/// <param name="WorkspaceRoot">The folder the thread worked in, when known.</param>
/// <param name="ModelId">The model the thread runs on, or null for the model chosen in Settings.</param>
/// <param name="HasCustomTitle">Whether the user renamed the thread.</param>
public sealed record AgentSessionSummary(
    string Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string? WorkspaceRoot,
    string? ModelId = null,
    bool HasCustomTitle = false);
