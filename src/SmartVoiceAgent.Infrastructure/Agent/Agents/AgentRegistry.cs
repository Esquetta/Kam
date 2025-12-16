using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using System.Collections.Concurrent;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AIAgent> _agents = new();
    private readonly ILogger<AgentRegistry> _logger;

    public AgentRegistry(ILogger<AgentRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterAgent(string name, AIAgent agent)
    {
        if (_agents.TryAdd(name, agent))
        {
            _logger.LogInformation("✓ Agent registered: {AgentName}", name);
        }
        else
        {
            _logger.LogWarning("⚠ Agent already exists: {AgentName}", name);
        }
    }

    public AIAgent GetAgent(string name)
    {
        if (_agents.TryGetValue(name, out var agent))
        {
            return agent;
        }
        throw new InvalidOperationException($"Agent not found: {name}");
    }

    public IEnumerable<string> GetAllAgentNames() => _agents.Keys;

    public bool IsAgentAvailable(string name) => _agents.ContainsKey(name);
}