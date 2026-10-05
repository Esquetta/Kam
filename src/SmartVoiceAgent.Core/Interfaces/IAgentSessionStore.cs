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

    /// <summary>Replaces a session's messages.</summary>
    Task SaveAsync(string sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Lists saved sessions, newest first.</summary>
    Task<IReadOnlyList<AgentSessionSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes a session.</summary>
    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}
