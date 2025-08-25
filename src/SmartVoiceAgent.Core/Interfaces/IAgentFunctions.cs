using AutoGen.Core;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IAgentFunctions
{
    IEnumerable<FunctionContract> GetFunctionContracts();
}