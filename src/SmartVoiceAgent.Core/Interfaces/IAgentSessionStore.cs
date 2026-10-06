using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Persists agent threads so they survive restarts.
/// </summary>
public interface IAgentSessionStore
{
    /// <summary>Loads a session's messages, or an empty list when it does not exist.</summary>
    Task<IReadOnlyList<ChatMessage>> LoadAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Replaces a session's messages. The title and model set for the session are kept.</summary>
    Task SaveAsync(string sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Lists saved sessions, newest first, without reading their messages.</summary>
    Task<IReadOnlyList<AgentSessionSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a session's title, model and size without reading its messages, or null when nothing is stored for it.
    /// </summary>
    Task<AgentSessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a session. A blank title goes back to the title taken from its first message.
    /// A session that has no messages yet keeps the title until it is first saved.
    /// </summary>
    Task RenameAsync(string sessionId, string? title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the model a session runs on; null uses the model chosen in Settings.
    /// A session that has no messages yet keeps the choice until it is first saved.
    /// </summary>
    Task SetModelAsync(string sessionId, string? modelId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a session.</summary>
    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}
