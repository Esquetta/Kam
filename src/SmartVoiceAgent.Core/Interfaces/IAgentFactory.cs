using Microsoft.Agents.AI;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IAgentFactory
{
    AIAgent CreateSystemAgent();
    Task<AIAgent> CreateTaskAgentAsync();
    AIAgent CreateResearchAgent();
    AIAgent CreateCoordinatorAgent();
    IAgentBuilder CreateCustomAgent();
}