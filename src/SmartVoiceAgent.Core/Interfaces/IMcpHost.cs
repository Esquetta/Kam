using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.Extensions;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Starts the configured MCP servers on demand and offers their tools to the agent.
/// </summary>
public interface IMcpHost
{
    /// <summary>Gets the user configuration file, <c>%AppData%/Kam/mcp.json</c>.</summary>
    string UserConfigPath { get; }

    /// <summary>Gets every configured server and its state.</summary>
    IReadOnlyList<McpServerState> Servers { get; }

    /// <summary>Raised when a server's state changes or the configuration is reloaded.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Connects servers that have not been started yet and returns the tools of every ready server.
    /// It waits only briefly for servers that are still starting; they keep starting and their tools
    /// are returned by a later call. A server that fails stays failed until it is restarted or the
    /// configuration is reloaded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the configuration again and disconnects servers that were removed or changed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects a server so the next agent turn starts it again.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    Task RestartAsync(string serverName);
}

/// <summary>
/// Supplies MCP server entries: the user's <c>mcp.json</c>, plugins, or a built-in integration.
/// </summary>
public interface IMcpServerSource
{
    /// <summary>
    /// Returns the entries this source currently contributes.
    /// </summary>
    IReadOnlyList<McpServerDefinition> GetServers();
}

/// <summary>
/// Looks up secrets saved in Kam's secret store, for <c>${secret:NAME}</c> references in MCP entries.
/// </summary>
public interface ISecretValueProvider
{
    /// <summary>
    /// Returns a secret, or <c>null</c> when it is not saved.
    /// </summary>
    /// <param name="name">The secret name.</param>
    string? GetSecret(string name);
}
