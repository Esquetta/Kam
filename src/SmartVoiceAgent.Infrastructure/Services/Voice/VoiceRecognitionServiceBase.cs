using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

public abstract class VoiceRecognitionServiceBase : IVoiceRecognitionService
{
    protected CircularAudioBuffer _audioBuffer;
    protected bool _isListening;
    protected bool _disposed;
    protected readonly object _lock = new();
    protected Timer _memoryCleanupTimer;

    protected readonly int _bufferCapacitySeconds = 30;
    protected readonly long _maxMemoryThreshold = 50 * 1024 * 1024;

    protected readonly double _voiceThreshold = 0.02; // RMS threshold
    protected readonly int _silenceTimeoutMs = 800;

    private DateTime _lastVoiceDetectedTime;
    private MemoryStream _currentSpeechStream;

    protected VoiceRecognitionServiceBase()
    {
        _audioBuffer = new CircularAudioBuffer(_bufferCapacitySeconds);
        _memoryCleanupTimer = new Timer(CleanupMemory, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    // Interface Events
    public event EventHandler<byte[]> OnVoiceCaptured;
    public event EventHandler<Exception> OnError;
    public event EventHandler OnListeningStarted;
    public event EventHandler OnListeningStopped;

    // Protected event invokers
    protected virtual void InvokeOnVoiceCaptured(byte[] data) => OnVoiceCaptured?.Invoke(this, data);
    protected virtual void InvokeOnError(Exception ex) => OnError?.Invoke(this, ex);
    protected virtual void InvokeOnListeningStarted() => OnListeningStarted?.Invoke(this, EventArgs.Empty);
    protected virtual void InvokeOnListeningStopped() => OnListeningStopped?.Invoke(this, EventArgs.Empty);

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

    protected abstract void StartListeningInternal();
    protected abstract void StopListeningInternal();
    protected abstract void CleanupPlatformResources();

    public void StartListening()
    {
        lock (_lock)
        {
            if (_isListening)
                throw new InvalidOperationException("Listening is already running.");

            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            _audioBuffer.Clear();
            _currentSpeechStream = new MemoryStream();
            StartListeningInternal();
            _isListening = true;
            OnListeningStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopListening()
    {
        lock (_lock)
        {
            if (!_isListening)
                return;

            StopListeningInternal();
        }
    }

    public void ClearBuffer()
    {
        lock (_lock)
        {
            _audioBuffer?.Clear();
        }
    }

    public long GetCurrentBufferSize()
    {
        lock (_lock)
        {
            return _audioBuffer?.Count ?? 0;
        }
    }

    protected virtual void AddAudioData(byte[] data)
    {
        if (data == null || data.Length == 0) return;

        lock (_lock)
        {
            _audioBuffer.Write(data);

            var rms = CalculateRms(data);
            if (rms >= _voiceThreshold)
            {
                _currentSpeechStream.Write(data, 0, data.Length);
                _lastVoiceDetectedTime = DateTime.Now;
            }
            else if (_currentSpeechStream.Length > 0)
            {
                var silenceDuration = (DateTime.Now - _lastVoiceDetectedTime).TotalMilliseconds;
                if (silenceDuration > _silenceTimeoutMs)
                {
                    var voiceData = _currentSpeechStream.ToArray();
                    _currentSpeechStream.Dispose();
                    _currentSpeechStream = new MemoryStream();
                    OnVoiceCaptured?.Invoke(this, voiceData);
                }
            }
        }
    }

    private double CalculateRms(byte[] buffer)
    {
        if (buffer.Length == 0) return 0;

        double sumSquares = 0;
        for (int i = 0; i < buffer.Length; i += 2)
        {
            if (i + 1 >= buffer.Length) break;
            short sample = BitConverter.ToInt16(buffer, i);
            double sample32 = sample / 32768.0;
            sumSquares += sample32 * sample32;
        }

        return Math.Sqrt(sumSquares / (buffer.Length / 2));
    }

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
            OnError?.Invoke(this, ex);
        }
    }

    protected virtual void CleanupResources()
    {
        try
        {
            _audioBuffer?.Clear();
            _currentSpeechStream?.Dispose();
            CleanupPlatformResources();
            _isListening = false;
        }
        catch { }
    }

    public void Dispose()
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

    public async Task<byte[]> RecordForDurationAsync(TimeSpan duration)
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
    // Ses verisini buffer'dan çekip temizleyen yardımcı metot
    protected virtual byte[] GetAndClearAudioData()
    {
        lock (_lock)
        {
            var data = _audioBuffer.ReadAll();
            _audioBuffer.Clear();
            return data;
        }
    }

    // Dinleme tamamlandığında çağrılan helper
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
}
