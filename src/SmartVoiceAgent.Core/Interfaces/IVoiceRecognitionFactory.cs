
namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Factory for creating platform-specific voice recognition services.
/// </summary>
public interface IVoiceRecognitionFactory
{
    /// <summary>
    /// Creates a recorder for the microphone chosen in Settings.
    /// </summary>
    IVoiceRecognitionService Create();
}
