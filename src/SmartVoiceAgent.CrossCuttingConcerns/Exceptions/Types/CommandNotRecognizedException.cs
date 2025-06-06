namespace SmartVoiceAgent.CrossCuttingConcerns.Exceptions.Types;

/// <summary>
/// Exception thrown when a voice command cannot be recognized.
/// </summary>
public class CommandNotRecognizedException : Exception
{
    public CommandNotRecognizedException() { }
    public CommandNotRecognizedException(string message) : base(message) { }
    public CommandNotRecognizedException(string message, Exception inner) : base(message, inner) { }
}
