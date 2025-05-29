using SmartVoiceAgent.Core.Enums;

public interface IIntentDetector
{
    /// <summary>
    /// Detects the intent from a given voice input asynchronously.
    /// </summary>
    /// <param name="voiceInput">The voice command input as text.</param>
    /// <returns>The detected command type.</returns>
    Task<CommandType> DetectIntentAsync(string voiceInput);
}
