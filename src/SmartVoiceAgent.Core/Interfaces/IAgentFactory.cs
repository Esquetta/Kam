using Microsoft.Agents.AI;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IAgentFactory
{
    AIAgent CreateSystemAgent();
    AIAgent CreateTaskAgent();
    AIAgent CreateResearchAgent();
    AIAgent CreateCoordinatorAgent();
    IAgentBuilder CreateCustomAgent();
}