namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for voice recognition operations.
/// </summary>
public interface IVoiceRecognitionService : IDisposable
{
    void StartRecording();
    void StopRecording();
    void ClearBuffer();
    long GetCurrentBufferSize();
    Task<byte[]> RecordForDurationAsync(TimeSpan duration);

    // Properties
    bool IsRecording { get; }

    // Events
    event EventHandler<byte[]> OnVoiceCaptured;
    event EventHandler<Exception> OnError;
    event EventHandler OnRecordingStarted;
    event EventHandler OnRecordingStopped;
}

