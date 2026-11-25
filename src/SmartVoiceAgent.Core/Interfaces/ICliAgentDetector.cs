using SmartVoiceAgent.Core.Models.Agents.CliAgents;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Finds the coding-agent CLIs on PATH and asks each for its version and sign-in state.
/// </summary>
public interface ICliAgentDetector
{
    /// <summary>
    /// Checks every supported CLI.
    /// </summary>
    /// <param name="cancellationToken">Stops the checks.</param>
    Task<IReadOnlyList<CliAgentStatus>> DetectAsync(CancellationToken cancellationToken = default);
}
