using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using System.Collections.Concurrent;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AIAgent> _agents = new();
    private readonly LoggerServiceBase _logger;

    public AgentRegistry(LoggerServiceBase logger)
    {
        _logger = logger;
    }

    public void RegisterAgent(string name, AIAgent agent)
    {
        if (_agents.TryAdd(name, agent))
        {
            _logger.Info($"Agent registered: {agent.Name}");
        }
        else
        {
            _logger.Warn($"Agent already exists: {agent.Name}");
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

    public IEnumerable<string> GetAllAgentNames()
    {
        return _agents.Keys;
    }

    public void UnregisterAgent(string name)
    {
        if (_agents.TryRemove(name, out _))
        {
            _logger.Info($"Agent unregistered: {name}");
        }
    }

    public bool IsAgentAvailable(string name)
    {
        return _agents.ContainsKey(name);
    }
}