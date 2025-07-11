using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;
public abstract class VoiceRecognitionServiceBase : IVoiceRecognitionService
{
    protected MemoryStream _memoryStream;
    protected bool _isRecording;
    protected bool _disposed;
    protected readonly object _lock = new object();

    // Events - explicit implementation to avoid CS0070
    private event EventHandler<byte[]> _onVoiceCaptured;
    private event EventHandler<Exception> _onError;
    private event EventHandler _onRecordingStarted;
    private event EventHandler _onRecordingStopped;

    public event EventHandler<byte[]> OnVoiceCaptured
    {
        add { _onVoiceCaptured += value; }
        remove { _onVoiceCaptured -= value; }
    }

    public event EventHandler<Exception> OnError
    {
        add { _onError += value; }
        remove { _onError -= value; }
    }

    public event EventHandler OnRecordingStarted
    {
        add { _onRecordingStarted += value; }
        remove { _onRecordingStarted -= value; }
    }

    public event EventHandler OnRecordingStopped
    {
        add { _onRecordingStopped += value; }
        remove { _onRecordingStopped -= value; }
    }

    // Protected helper methods for invoking events
    protected virtual void InvokeOnVoiceCaptured(byte[] data) => _onVoiceCaptured?.Invoke(this, data);
    protected virtual void InvokeOnError(Exception ex) => _onError?.Invoke(this, ex);
    protected virtual void InvokeOnRecordingStarted() => _onRecordingStarted?.Invoke(this, EventArgs.Empty);
    protected virtual void InvokeOnRecordingStopped() => _onRecordingStopped?.Invoke(this, EventArgs.Empty);

    // Properties
    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _isRecording;
            }
        }
    }

    // Abstract methods that must be implemented by derived classes
    protected abstract void StartRecordingInternal();
    protected abstract void StopRecordingInternal();
    protected abstract void CleanupPlatformResources();

    // Common public methods
    public virtual void StartRecording()
    {
        lock (_lock)
        {
            try
            {
                if (_isRecording)
                    throw new InvalidOperationException("Kayıt zaten devam ediyor.");

                if (_disposed)
                    throw new ObjectDisposedException(GetType().Name);

                _memoryStream = new MemoryStream();
                StartRecordingInternal();
                _isRecording = true;
                InvokeOnRecordingStarted();
            }
            catch (Exception ex)
            {
                InvokeOnError(ex);
                CleanupResources();
            }
        }
    }

    public virtual void StopRecording()
    {
        lock (_lock)
        {
            try
            {
                if (!_isRecording)
                    return;

                StopRecordingInternal();
            }
            catch (Exception ex)
            {
                InvokeOnError(ex);
            }
        }
    }

    public virtual void ClearBuffer()
    {
        lock (_lock)
        {
            if (_memoryStream != null)
            {
                _memoryStream.SetLength(0);
                _memoryStream.Position = 0;
            }
        }
    }

    public virtual long GetCurrentBufferSize()
    {
        lock (_lock)
        {
            return _memoryStream?.Length ?? 0;
        }
    }

    public virtual async Task<byte[]> RecordForDurationAsync(TimeSpan duration)
    {
        var tcs = new TaskCompletionSource<byte[]>();

        EventHandler<byte[]> captureHandler = null;
        EventHandler<Exception> errorHandler = null;

        captureHandler = (s, data) =>
        {
            OnVoiceCaptured -= captureHandler;
            OnError -= errorHandler;
            tcs.SetResult(data);
        };

        errorHandler = (s, ex) =>
        {
            OnVoiceCaptured -= captureHandler;
            OnError -= errorHandler;
            tcs.SetException(ex);
        };

        OnVoiceCaptured += captureHandler;
        OnError += errorHandler;

        StartRecording();

        // Belirtilen süre sonra kaydı durdur
        _ = Task.Delay(duration).ContinueWith(_ => StopRecording());

        return await tcs.Task;
    }

    protected virtual void OnRecordingComplete(byte[] audioData)
    {
        try
        {
            _isRecording = false;

            if (audioData != null && audioData.Length > 0)
            {
                InvokeOnVoiceCaptured(audioData);
            }

            InvokeOnRecordingStopped();
        }
        catch (Exception ex)
        {
            InvokeOnError(ex);
        }
        finally
        {
            CleanupResources();
        }
    }

    protected virtual void CleanupResources()
    {
        try
        {
            _memoryStream?.Dispose();
            _memoryStream = null;
            CleanupPlatformResources();
            _isRecording = false;
        }
        catch
        {
            // Cleanup sırasında hata olsa bile devam et
        }
    }

    public virtual void Dispose()
    {
        if (!_disposed)
        {
            if (_isRecording)
                StopRecording();

            CleanupResources();
            _disposed = true;
        }
    }
}