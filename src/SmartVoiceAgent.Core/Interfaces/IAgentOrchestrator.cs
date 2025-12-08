using SmartVoiceAgent.Core.Dtos.Agent;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<string> ExecuteAsync(string userRequest);
    IAsyncEnumerable<AgentExecutionUpdate> ExecuteStreamAsync(string userRequest);
}
