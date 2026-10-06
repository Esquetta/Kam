using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Runs agent turns: the model calls tools, sees the results and continues until it answers.
/// </summary>
public interface IAgentRuntime
{
    /// <summary>
    /// Sends a user message to a session and streams what happens until the turn ends.
    /// </summary>
    /// <param name="sessionId">The session to continue; a new one is created when it does not exist.</param>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="cancellationToken">Stops the turn.</param>
    IAsyncEnumerable<AgentEvent> RunTurnAsync(
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a pending approval request.
    /// </summary>
    /// <param name="requestId">The id from <see cref="AgentApprovalRequested"/>.</param>
    /// <param name="approved">Whether the call may run.</param>
    /// <param name="alwaysAllow">Also save a rule so this tool no longer asks.</param>
    /// <returns><c>true</c> when the request was still pending.</returns>
    bool ResolveApproval(string requestId, bool approved, bool alwaysAllow = false);
}
