namespace SmartVoiceAgent.Core.Exceptions;

/// <summary>
/// Exception thrown during voice recognition errors.
/// </summary>
public class VoiceRecognitionException : Exception
{
    public VoiceRecognitionException() { }
    public VoiceRecognitionException(string message) : base(message) { }
    public VoiceRecognitionException(string message, Exception inner) : base(message, inner) { }
}
