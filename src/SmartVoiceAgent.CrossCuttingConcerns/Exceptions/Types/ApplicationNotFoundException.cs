namespace SmartVoiceAgent.CrossCuttingConcerns.Exceptions.Types;

/// <summary>
/// Exception thrown when a requested application is not found.
/// </summary>
public class ApplicationNotFoundException : Exception
{
    public ApplicationNotFoundException() { }
    public ApplicationNotFoundException(string message) : base(message) { }
    public ApplicationNotFoundException(string message,Exception inner) : base(message,inner) { }
}

