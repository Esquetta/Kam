namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for voice recognition operations.
/// </summary>
public interface IVoiceRecognitionService : IDisposable
{
    void StartListening(); // continuous
    void StopListening();
    void ClearBuffer();
    long GetCurrentBufferSize();
    Task<byte[]> RecordForDurationAsync(TimeSpan duration); // istek bazlı 

    // Properties
    bool IsListening { get; }

    // Events
    event EventHandler<byte[]> OnVoiceCaptured;
    event EventHandler<Exception> OnError;
    event EventHandler OnListeningStarted;
    event EventHandler OnListeningStopped;
}


