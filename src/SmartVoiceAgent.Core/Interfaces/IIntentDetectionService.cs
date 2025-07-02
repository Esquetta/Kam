using SmartVoiceAgent.Core.Entities;

public interface IIntentDetectionService
{
    /// <summary>
    /// Detects the intent from a given voice input asynchronously.
    /// </summary>
    /// <param name="voiceInput">The voice command input as text.</param>
    /// <returns>The detected command type.</returns>
    Task<IntentResult> DetectIntentAsync(string text, string language, CancellationToken cancellationToken = default);
}
