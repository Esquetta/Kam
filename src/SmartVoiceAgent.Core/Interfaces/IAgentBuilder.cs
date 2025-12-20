using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IAgentBuilder
{
    IAgentBuilder WithName(string name);
    IAgentBuilder WithInstructions(string instructions);
    IAgentBuilder WithTools<TToolClass>() where TToolClass : class;
    IAgentBuilder WithTools(IEnumerable<AIFunction> tools);
    Task<IAgentBuilder> WithToolsAsync<TToolClass>() where TToolClass : class;
    AIAgent Build();
    Task<AIAgent> BuildAsync();
}