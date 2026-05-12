using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IRuntimeAgentReadOnlyToolService
{
    Task<IReadOnlyList<RuntimeAgentToolObservation>> ExecuteAsync(
        IReadOnlyList<RuntimeAgentReadOnlyToolRequest> requests,
        CancellationToken cancellationToken = default);
}
