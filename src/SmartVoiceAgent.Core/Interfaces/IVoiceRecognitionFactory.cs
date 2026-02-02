namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Factory for creating platform-specific voice recognition services.
/// </summary>
public interface IVoiceRecognitionFactory
{
    /// <summary>
    /// Creates a new voice recognition service instance.
    /// </summary>
    IVoiceRecognitionService Create();
}
