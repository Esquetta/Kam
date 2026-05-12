using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IRuntimeAgentFactory
{
    Task<RuntimeAgentResult> RunAsync(
        RuntimeAgentRequest request,
        CancellationToken cancellationToken = default);
}
