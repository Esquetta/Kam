using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

public abstract class VoiceRecognitionServiceBase : IVoiceRecognitionService
{
    protected CircularAudioBuffer _audioBuffer;
    protected bool _isListening;
    protected bool _disposed;
    protected readonly object _lock = new object();
    protected Timer _memoryCleanupTimer;

    // Configuration
    protected readonly int _bufferCapacitySeconds = 30; // 30 saniye buffer
    protected readonly long _maxMemoryThreshold = 50 * 1024 * 1024; // 50MB

    protected VoiceRecognitionServiceBase()
    {
        _audioBuffer = new CircularAudioBuffer(_bufferCapacitySeconds);

        // Hafıza temizleme timer'ı - her 5 dakikada bir
        _memoryCleanupTimer = new Timer(CleanupMemory, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    // Events
    private event EventHandler<byte[]> _onVoiceCaptured;
    private event EventHandler<Exception> _onError;
    private event EventHandler _onListeningStarted;
    private event EventHandler _onListeningStopped;

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

    public event EventHandler OnListeningStarted
    {
        add { _onListeningStarted += value; }
        remove { _onListeningStarted -= value; }
    }

    public event EventHandler OnListeningStopped
    {
        add { _onListeningStopped += value; }
        remove { _onListeningStopped -= value; }
    }

    // Event invoker helpers
    protected virtual void InvokeOnVoiceCaptured(byte[] data) => _onVoiceCaptured?.Invoke(this, data);
    protected virtual void InvokeOnError(Exception ex) => _onError?.Invoke(this, ex);
    protected virtual void InvokeOnListeningStarted() => _onListeningStarted?.Invoke(this, EventArgs.Empty);
    protected virtual void InvokeOnListeningStopped() => _onListeningStopped?.Invoke(this, EventArgs.Empty);

    // Properties
    public bool IsListening
    {
        get
        {
            lock (_lock)
            {
                return _isListening;
            }
        }
    }

    // Abstract methods
    protected abstract void StartListeningInternal();
    protected abstract void StopListeningInternal();
    protected abstract void CleanupPlatformResources();

    // Common public methods
    public virtual void StartListening()
    {
        lock (_lock)
        {
            try
            {
                if (_isListening)
                    throw new InvalidOperationException("Dinleme zaten devam ediyor.");

                if (_disposed)
                    throw new ObjectDisposedException(GetType().Name);

                _audioBuffer.Clear();
                StartListeningInternal();
                _isListening = true;

                InvokeOnListeningStarted();
            }
            catch (Exception ex)
            {
                InvokeOnError(ex);
                CleanupResources();
            }
        }
    }

    public virtual void StopListening()
    {
        lock (_lock)
        {
            try
            {
                if (!_isListening)
                    return;

                StopListeningInternal();
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
            _audioBuffer?.Clear();
        }
    }

    public virtual long GetCurrentBufferSize()
    {
        lock (_lock)
        {
            return _audioBuffer?.Count ?? 0;
        }
    }

    // Memory cleanup
    private void CleanupMemory(object state)
    {
        try
        {
            var currentMemory = GC.GetTotalMemory(false);

            if (currentMemory > _maxMemoryThreshold)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        catch (Exception ex)
        {
            InvokeOnError(ex);
        }
    }

    // Ses verisi buffer'dan alınması
    public virtual byte[] GetAudioData()
    {
        lock (_lock)
        {
            return _audioBuffer.ReadAll();
        }
    }

    public virtual byte[] GetAndClearAudioData()
    {
        lock (_lock)
        {
            var data = _audioBuffer.ReadAll();
            _audioBuffer.Clear();
            return data;
        }
    }

    protected virtual void AddAudioData(byte[] data)
    {
        if (data != null && data.Length > 0)
        {
            lock (_lock)
            {
                _audioBuffer.Write(data);
            }
        }
    }

    protected virtual void OnListeningComplete(byte[] audioData)
    {
        try
        {
            _isListening = false;

            if (audioData != null && audioData.Length > 0)
            {
                InvokeOnVoiceCaptured(audioData);
            }

            InvokeOnListeningStopped();
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
            _audioBuffer?.Clear();
            CleanupPlatformResources();
            _isListening = false;
        }
        catch
        {
            // ignore
        }
    }

    public virtual void Dispose()
    {
        if (!_disposed)
        {
            if (_isListening)
                StopListening();

            _memoryCleanupTimer?.Dispose();
            CleanupResources();
            _disposed = true;
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

        StartListening();

        _ = Task.Delay(duration).ContinueWith(_ => StopListening());

        return await tcs.Task;
    }
}
