using AutoGen.Core;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IAgentFunctions
{
    IDictionary<string, Func<string,Task<string>>> GetFunctionMap();
}