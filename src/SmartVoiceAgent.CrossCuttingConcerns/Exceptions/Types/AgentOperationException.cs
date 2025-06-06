namespace SmartVoiceAgent.CrossCuttingConcerns.Exceptions.Types;

/// <summary>
/// General exception for errors occurring during agent operations.
/// </summary>
public class AgentOperationException : Exception
{
    public AgentOperationException() { }
    public AgentOperationException(string message) : base(message) { }
    public AgentOperationException(string message, Exception inner) : base(message, inner) { }
}
