using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Supplies tools to the agent runtime, for example built-in skills, MCP servers or plugins.
/// </summary>
public interface IAgentToolProvider
{
    /// <summary>
    /// Returns the tools this provider currently offers.
    /// </summary>
    /// <param name="cancellationToken">Cancels discovery, such as listing tools on a remote server.</param>
    Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default);
}
