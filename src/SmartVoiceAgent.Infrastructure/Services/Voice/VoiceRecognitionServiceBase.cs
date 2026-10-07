using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Shared recorder logic: platform subclasses deliver 16 kHz mono 16-bit PCM through <see cref="AddAudioData"/>,
/// and this class finds utterances with <see cref="VoiceActivityDetector"/>. A recorder can be started and
/// stopped any number of times.
/// </summary>
public abstract class VoiceRecognitionServiceBase : IVoiceRecognitionService
{
    private static readonly TimeSpan MinimumUndetectedSpeech = TimeSpan.FromMilliseconds(600);

    // _lifecycle serializes start and stop; _gate guards the audio state the capture thread writes. Platform
    // capture is started and stopped outside _gate, so a capture thread waiting on it can always finish.
    private readonly object _lifecycle = new();
    private readonly object _gate = new();
    private readonly CircularAudioBuffer _recent = CircularAudioBuffer.ForAudio(30);
    private readonly VoiceActivityDetector _detector;
    private bool _isListening;
    private bool _capturedThisSession;
    private bool _disposed;

    /// <summary>
    /// Creates the recorder.
    /// </summary>
    /// <param name="activityOptions">The speech detection tuning, or the defaults.</param>
    protected VoiceRecognitionServiceBase(VoiceActivityOptions? activityOptions = null)
    {
        _detector = new VoiceActivityDetector(activityOptions);
    }

    /// <inheritdoc />
    public event EventHandler<byte[]>? OnVoiceCaptured;

    /// <inheritdoc />
    public event EventHandler<Exception>? OnError;

    /// <inheritdoc />
    public event EventHandler? OnListeningStarted;

    /// <inheritdoc />
    public event EventHandler? OnListeningStopped;

    /// <inheritdoc />
    public event EventHandler<float>? OnAudioLevel;

    /// <inheritdoc />
    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _isListening;
            }
        }
    }

    /// <summary>
    /// Starts the platform capture.
    /// </summary>
    protected abstract void StartListeningInternal();

    /// <summary>
    /// Stops the platform capture; audio that arrives afterwards is ignored.
    /// </summary>
    protected abstract void StopListeningInternal();

    /// <summary>
    /// Releases the platform capture.
    /// </summary>
    protected abstract void CleanupPlatformResources();

    /// <inheritdoc />
    public void StartListening()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsListening)
            {
                return;
            }

            lock (_gate)
            {
                _recent.Clear();
                _detector.Reset();
                _capturedThisSession = false;
                _isListening = true;
            }

            try
            {
                StartListeningInternal();
            }
            catch
            {
                lock (_gate)
                {
                    _isListening = false;
                }

                CleanupPlatformResources();
                throw;
            }
        }

        OnListeningStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void StopListening()
    {
        byte[]? pending;
        lock (_lifecycle)
        {
            lock (_gate)
            {
                if (!_isListening)
                {
                    return;
                }

                _isListening = false;
            }

            try
            {
                StopListeningInternal();
            }
            finally
            {
                CleanupPlatformResources();
            }

            lock (_gate)
            {
                pending = _detector.Flush();
                if (pending is null && !_capturedThisSession && HeardSomething())
                {
                    // Quiet speech the detector didn't pick up: hand over the whole recording and let recognition decide.
                    pending = _recent.ReadAll();
                }

                _recent.Clear();
                _detector.Reset();
            }
        }

        if (pending is { Length: > 0 })
        {
            OnVoiceCaptured?.Invoke(this, pending);
        }

        OnListeningStopped?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ClearBuffer()
    {
        lock (_gate)
        {
            _recent.Clear();
        }
    }

    /// <inheritdoc />
    public long GetCurrentBufferSize()
    {
        lock (_gate)
        {
            return _recent.Count;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> RecordForDurationAsync(TimeSpan duration)
    {
        var captured = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCaptured(object? sender, byte[] audio) => captured.TrySetResult(audio);
        void OnFailed(object? sender, Exception error) => captured.TrySetException(error);

        OnVoiceCaptured += OnCaptured;
        OnError += OnFailed;
        try
        {
            StartListening();
            var finished = await Task.WhenAny(captured.Task, Task.Delay(duration)).ConfigureAwait(false);
            StopListening();
            return finished == captured.Task || captured.Task.IsCompleted ? await captured.Task.ConfigureAwait(false) : [];
        }
        finally
        {
            OnVoiceCaptured -= OnCaptured;
            OnError -= OnFailed;
        }
    }

    /// <summary>
    /// Feeds recorded audio. Platform subclasses call this from their capture thread.
    /// </summary>
    /// <param name="pcm">16 kHz mono 16-bit little-endian samples.</param>
    protected void AddAudioData(ReadOnlySpan<byte> pcm)
    {
        if (pcm.IsEmpty)
        {
            return;
        }

        IReadOnlyList<byte[]> utterances;
        float level;
        lock (_gate)
        {
            if (!_isListening)
            {
                return;
            }

            _recent.Write(pcm);
            utterances = _detector.Process(pcm);
            _capturedThisSession |= utterances.Count > 0;
            level = (float)_detector.Level;
        }

        OnAudioLevel?.Invoke(this, level);
        foreach (var utterance in utterances)
        {
            OnVoiceCaptured?.Invoke(this, utterance);
        }
    }

    /// <summary>
    /// Feeds recorded audio.
    /// </summary>
    /// <param name="pcm">16 kHz mono 16-bit little-endian samples.</param>
    protected void AddAudioData(byte[] pcm) => AddAudioData(pcm.AsSpan());

    /// <summary>
    /// Reports a capture failure and stops recording.
    /// </summary>
    /// <param name="error">The failure.</param>
    protected void ReportError(Exception error)
    {
        OnError?.Invoke(this, error);
        StopListening();
    }

    /// <summary>
    /// Raises <see cref="OnError"/> without stopping.
    /// </summary>
    /// <param name="error">The failure.</param>
    protected void InvokeOnError(Exception error) => OnError?.Invoke(this, error);

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Stops recording and releases the capture.
    /// </summary>
    /// <param name="disposing">Whether managed resources are released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            StopListening();
            lock (_lifecycle)
            {
                CleanupPlatformResources();
            }
        }

        _disposed = true;
    }

    private bool HeardSomething()
    {
        return _detector.PeakLevel >= 0.003
            && WaveAudio.Duration(_recent.Count) >= MinimumUndetectedSpeech;
    }
}
