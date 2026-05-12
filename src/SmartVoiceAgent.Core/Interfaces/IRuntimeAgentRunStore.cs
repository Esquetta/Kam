using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IRuntimeAgentRunStore
{
    RuntimeAgentRun Start(RuntimeAgentRequest request, string modelId);

    RuntimeAgentRun Complete(string runId, string response);

    RuntimeAgentRun Fail(string runId, string errorMessage);

    RuntimeAgentRun Cancel(string runId, string reason);

    RuntimeAgentRun? Get(string runId);

    IReadOnlyList<RuntimeAgentRun> List(int maxCount = 50);
}
