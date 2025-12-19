using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public class AgentBuilder : IAgentBuilder
{
    private readonly IChatClient _chatClient;
    private readonly IServiceProvider _serviceProvider;
    private string _name = "Agent";
    private string _instructions = "You are a helpful assistant.";
    private readonly List<AIFunction> _tools = new();

    public AgentBuilder(IChatClient chatClient, IServiceProvider serviceProvider)
    {
        _chatClient = chatClient;
        _serviceProvider = serviceProvider;
    }

    public IAgentBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public IAgentBuilder WithInstructions(string instructions)
    {
        _instructions = instructions;
        return this;
    }

    public IAgentBuilder WithTools<TToolClass>() where TToolClass : class
    {
        // Get tool class instance from DI
        var toolInstance = _serviceProvider.GetRequiredService<TToolClass>();

        // Get tools from the GetTools() method
        var getToolsMethod = typeof(TToolClass).GetMethod("GetTools");
        if (getToolsMethod != null)
        {
            var tools = getToolsMethod.Invoke(toolInstance, null) as IEnumerable<AIFunction>;
            if (tools != null)
            {
                _tools.AddRange(tools);
            }
        }

        return this;
    }

    public IAgentBuilder WithTools(IEnumerable<AIFunction> tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    public AIAgent Build()
    {
        return _chatClient.CreateAIAgent(
            name: _name,
            instructions: _instructions,
            tools: _tools.ToArray()
        );
    }
}