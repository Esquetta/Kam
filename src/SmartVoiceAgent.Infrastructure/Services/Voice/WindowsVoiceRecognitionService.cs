using NAudio.Wave;
using SmartVoiceAgent.Core.Interfaces;

public class WindowsVoiceRecognitionService : IVoiceRecognitionService, IDisposable
{
    private WaveInEvent _waveIn;
    private MemoryStream _memoryStream;
    private bool _isRecording;
    private bool _disposed;

    // Interface'den gelen event
    public event EventHandler<byte[]> OnVoiceCaptured;

    // Ek event'ler (opsiyonel)
    public event EventHandler<Exception> OnError;
    public event EventHandler OnRecordingStarted;
    public event EventHandler OnRecordingStopped;

    // Interface metodları
    public void StartRecording()
    {
        try
        {
            if (_isRecording)
            {
                throw new InvalidOperationException("Kayıt zaten devam ediyor.");
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WindowsVoiceRecognitionService));
            }

            _memoryStream = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1), // 16-bit, mono, 16kHz
                BufferMilliseconds = 100 // Buffer boyutu
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStoppedHandler;

            _waveIn.StartRecording();
            _isRecording = true;

            OnRecordingStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            CleanupResources();
        }
    }

    public void StopRecording()
    {
        try
        {
            if (!_isRecording)
            {
                return; // Zaten durmuş
            }

            _waveIn?.StopRecording();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
        }
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        try
        {
            if (_memoryStream != null && e.BytesRecorded > 0)
            {
                _memoryStream.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
        }
    }

    private void OnRecordingStoppedHandler(object sender, StoppedEventArgs e)
    {
        try
        {
            _isRecording = false;

            if (e.Exception != null)
            {
                OnError?.Invoke(this, e.Exception);
                return;
            }

            if (_memoryStream != null && _memoryStream.Length > 0)
            {
                var audioData = _memoryStream.ToArray();
                OnVoiceCaptured?.Invoke(this, audioData);
            }

            OnRecordingStopped?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
        }
        finally
        {
            CleanupResources();
        }
    }

    private void CleanupResources()
    {
        try
        {
            _memoryStream?.Dispose();
            _memoryStream = null;

            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStoppedHandler;
                _waveIn.Dispose();
                _waveIn = null;
            }

            _isRecording = false;
        }
        catch
        {
            // Cleanup sırasında hata olsa bile devam et
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_isRecording)
            {
                StopRecording();
            }

            CleanupResources();
            _disposed = true;
        }
    }

    // Ek özellikler (interface dışı)
    public bool IsRecording => _isRecording;

    // Memory optimizasyonu için
    public void ClearBuffer()
    {
        if (_memoryStream != null)
        {
            _memoryStream.SetLength(0);
            _memoryStream.Position = 0;
        }
    }

    // Anlık ses verisi boyutu kontrolü
    public long GetCurrentBufferSize()
    {
        return _memoryStream?.Length ?? 0;
    }

    // Belirli süre boyunca kayıt al (memory-only)
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

        StartRecording();

        // Belirtilen süre sonra kaydı durdur
        _ = Task.Delay(duration).ContinueWith(_ => StopRecording());

        return await tcs.Task;
    }
}