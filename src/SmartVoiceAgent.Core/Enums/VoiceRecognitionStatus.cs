namespace SmartVoiceAgent.Core.Enums;

/// <summary>
/// Represents the current state of the voice recognition engine.
/// </summary>
public enum VoiceRecognitionStatus
{
    Idle,
    Listening,
    Processing,
    Error
}
