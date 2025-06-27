namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for voice recognition operations.
/// </summary>
public interface IVoiceRecognitionService
{
    void StartRecording();
    void StopRecording();

    event EventHandler<byte[]> OnVoiceCaptured;
}

