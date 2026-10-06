namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Records the microphone and cuts the stream into utterances: speech that ends with a pause.
/// Audio is 16 kHz mono 16-bit PCM.
/// </summary>
public interface IVoiceRecognitionService : IDisposable
{
    /// <summary>
    /// Starts recording. Does nothing while already recording.
    /// </summary>
    void StartListening();

    /// <summary>
    /// Stops recording. Speech still in progress is delivered through <see cref="OnVoiceCaptured"/> first.
    /// </summary>
    void StopListening();

    /// <summary>
    /// Forgets the recorded audio.
    /// </summary>
    void ClearBuffer();

    /// <summary>
    /// Returns how many bytes of recent audio are kept.
    /// </summary>
    long GetCurrentBufferSize();

    /// <summary>
    /// Records until the first utterance ends or <paramref name="duration"/> passes.
    /// </summary>
    /// <param name="duration">The longest time to record.</param>
    Task<byte[]> RecordForDurationAsync(TimeSpan duration);

    /// <summary>
    /// Gets whether the microphone is being recorded.
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// Raised with each utterance.
    /// </summary>
    event EventHandler<byte[]>? OnVoiceCaptured;

    /// <summary>
    /// Raised when the microphone fails.
    /// </summary>
    event EventHandler<Exception>? OnError;

    /// <summary>
    /// Raised when recording starts.
    /// </summary>
    event EventHandler? OnListeningStarted;

    /// <summary>
    /// Raised when recording stops.
    /// </summary>
    event EventHandler? OnListeningStopped;

    /// <summary>
    /// Raised with the input level, 0..1, about fifty times a second while recording.
    /// </summary>
    event EventHandler<float>? OnAudioLevel;
}
