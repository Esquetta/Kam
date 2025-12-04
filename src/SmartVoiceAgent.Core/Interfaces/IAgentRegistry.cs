using Microsoft.Agents.AI;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IAgentRegistry
{
    AIAgent GetAgent(string name);
    IEnumerable<string> GetAllAgentNames();
    void RegisterAgent(string name, AIAgent agent);
    void UnregisterAgent(string name);
    bool IsAgentAvailable(string name);
}