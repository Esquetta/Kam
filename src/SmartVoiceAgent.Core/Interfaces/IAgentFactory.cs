using Microsoft.Agents.AI;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IAgentFactory
{
    AIAgent CreateSystemAgent();
    Task<AIAgent> CreateTaskAgentAsync(CancellationToken cancellationToken = default);
    AIAgent CreateResearchAgent();
    AIAgent CreateCommunicationAgent();
    AIAgent CreateCoordinatorAgent();
    IAgentBuilder CreateCustomAgent();
}
