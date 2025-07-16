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

    // Geliştirilmiş ses algılama parametreleri
    protected readonly double _voiceThreshold = 0.005; // Çok daha düşük threshold
    protected readonly double _noiseFloor = 0.001; // Minimum gürültü seviyesi
    protected readonly int _silenceTimeoutMs = 1000; // Sessizlik timeout'u
    protected readonly int _minSpeechLengthMs = 300; // Minimum konuşma uzunluğu
    protected readonly int _preBufferMs = 200; // Konuşma öncesi buffer
    protected readonly int _postBufferMs = 300; // Konuşma sonrası buffer

    // Gelişmiş algılama değişkenleri
    private DateTime _lastVoiceDetectedTime;
    private DateTime _firstVoiceDetectedTime;
    private MemoryStream _currentSpeechStream;
    private Queue<byte[]> _preBuffer; // Konuşma başlangıcını yakalamak için
    private Queue<double> _rmsHistory; // RMS geçmişi
    private readonly int _rmsHistorySize = 10;
    private readonly int _preBufferSize = 20; // 20 chunk pre-buffer
    private double _adaptiveThreshold;
    private double _backgroundNoiseLevel;
    private int _voiceFrameCount;
    private int _silenceFrameCount;
    private readonly int _minVoiceFrames = 3; // Minimum ses frame sayısı

    protected VoiceRecognitionServiceBase()
    {
        _audioBuffer = new CircularAudioBuffer(_bufferCapacitySeconds);
        _preBuffer = new Queue<byte[]>();
        _rmsHistory = new Queue<double>();
        _adaptiveThreshold = _voiceThreshold;
        _backgroundNoiseLevel = _noiseFloor;
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
            _preBuffer.Clear();
            _rmsHistory.Clear();
            _adaptiveThreshold = _voiceThreshold;
            _backgroundNoiseLevel = _noiseFloor;
            _voiceFrameCount = 0;
            _silenceFrameCount = 0;

            StartListeningInternal();
            _isListening = true;
            InvokeOnListeningStarted();
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
            _preBuffer?.Clear();
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

            // RMS ve diğer ses özelliklerini hesapla
            var rms = CalculateRms(data);
            var energy = CalculateEnergy(data);

            // Arka plan gürültü seviyesini güncelle
            UpdateBackgroundNoise(rms);

            // Adaptif threshold'u güncelle
            UpdateAdaptiveThreshold(rms);

            // Ses algılama mantığı
            var isVoiceDetected = DetectVoice(rms, energy);

            // Pre-buffer'ı yönet
            ManagePreBuffer(data);

            if (isVoiceDetected)
            {
                HandleVoiceDetection(data, rms);
            }
            else
            {
                HandleSilenceDetection(data);
            }
        }
    }

    private double CalculateRms(byte[] buffer)
    {
        if (buffer.Length == 0) return 0;

        double sumSquares = 0;
        int sampleCount = 0;

        // 16-bit stereo/mono için düzeltilmiş hesaplama
        for (int i = 0; i < buffer.Length - 1; i += 2)
        {
            short sample = BitConverter.ToInt16(buffer, i);
            double normalizedSample = sample / 32768.0; // 16-bit için normalize
            sumSquares += normalizedSample * normalizedSample;
            sampleCount++;
        }

        return sampleCount > 0 ? Math.Sqrt(sumSquares / sampleCount) : 0;
    }

    private double CalculateEnergy(byte[] buffer)
    {
        if (buffer.Length == 0) return 0;

        double totalEnergy = 0;
        int sampleCount = 0;

        for (int i = 0; i < buffer.Length - 1; i += 2)
        {
            short sample = BitConverter.ToInt16(buffer, i);
            totalEnergy += Math.Abs(sample);
            sampleCount++;
        }

        return sampleCount > 0 ? totalEnergy / sampleCount : 0;
    }

    private void UpdateBackgroundNoise(double currentRms)
    {
        // Arka plan gürültüsünü yavaşça güncelle
        const double alpha = 0.05; // Çok yavaş güncelleme
        _backgroundNoiseLevel = alpha * currentRms + (1 - alpha) * _backgroundNoiseLevel;
    }

    private void UpdateAdaptiveThreshold(double currentRms)
    {
        // RMS geçmişini güncelle
        _rmsHistory.Enqueue(currentRms);
        if (_rmsHistory.Count > _rmsHistorySize)
        {
            _rmsHistory.Dequeue();
        }

        // Adaptif threshold'u hesapla
        if (_rmsHistory.Count >= 5)
        {
            var averageRms = _rmsHistory.Average();
            var maxRms = _rmsHistory.Max();

            // Threshold'u arka plan gürültüsünün 2-3 katı olarak ayarla
            _adaptiveThreshold = Math.Max(
                _voiceThreshold,
                Math.Max(_backgroundNoiseLevel * 2.5, averageRms * 1.5)
            );

            // Çok yüksek olmasın
            _adaptiveThreshold = Math.Min(_adaptiveThreshold, 0.1);
        }
    }

    private bool DetectVoice(double rms, double energy)
    {
        // Çoklu kriter ses algılama
        var rmsCondition = rms > _adaptiveThreshold;
        var energyCondition = energy > _backgroundNoiseLevel * 1000;
        var relativeCondition = rms > _backgroundNoiseLevel * 3; // Arka plan gürültüsünün 3 katı

        // Debug için
        if (rmsCondition || energyCondition || relativeCondition)
        {
            System.Diagnostics.Debug.WriteLine($"Voice Detection - RMS: {rms:F6}, Threshold: {_adaptiveThreshold:F6}, Energy: {energy:F0}, Background: {_backgroundNoiseLevel:F6}");
        }

        return rmsCondition || energyCondition || relativeCondition;
    }

    private void ManagePreBuffer(byte[] data)
    {
        // Pre-buffer'a ekle
        _preBuffer.Enqueue((byte[])data.Clone());

        // Pre-buffer boyutunu kontrol et
        while (_preBuffer.Count > _preBufferSize)
        {
            _preBuffer.Dequeue();
        }
    }

    private void HandleVoiceDetection(byte[] data, double rms)
    {
        var currentTime = DateTime.Now;

        if (_currentSpeechStream.Length == 0)
        {
            // İlk ses algılama - pre-buffer'ı ekle
            _firstVoiceDetectedTime = currentTime;

            // Pre-buffer'daki tüm veriyi ekle
            foreach (var preData in _preBuffer)
            {
                _currentSpeechStream.Write(preData, 0, preData.Length);
            }

            System.Diagnostics.Debug.WriteLine($"Voice started - RMS: {rms:F6}");
        }

        // Mevcut veriyi ekle
        _currentSpeechStream.Write(data, 0, data.Length);
        _lastVoiceDetectedTime = currentTime;
        _voiceFrameCount++;
        _silenceFrameCount = 0;
    }

    private void HandleSilenceDetection(byte[] data)
    {
        if (_currentSpeechStream.Length > 0)
        {
            _silenceFrameCount++;

            var silenceDuration = (DateTime.Now - _lastVoiceDetectedTime).TotalMilliseconds;
            var speechDuration = (DateTime.Now - _firstVoiceDetectedTime).TotalMilliseconds;

            // Post-buffer için biraz sessizlik ekle
            if (silenceDuration < _postBufferMs)
            {
                _currentSpeechStream.Write(data, 0, data.Length);
            }

            // Ses bitişini kontrol et
            if (silenceDuration > _silenceTimeoutMs && _voiceFrameCount >= _minVoiceFrames)
            {
                // Konuşma yeterince uzun mu?
                if (speechDuration > _minSpeechLengthMs)
                {
                    var voiceData = _currentSpeechStream.ToArray();

                    System.Diagnostics.Debug.WriteLine($"Voice captured - Duration: {speechDuration}ms, Size: {voiceData.Length} bytes");

                    // Event'i tetikle
                    InvokeOnVoiceCaptured(voiceData);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Voice too short - Duration: {speechDuration}ms");
                }

                // Stream'i sıfırla
                _currentSpeechStream.Dispose();
                _currentSpeechStream = new MemoryStream();
                _voiceFrameCount = 0;
                _silenceFrameCount = 0;
            }
        }
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
            InvokeOnError(ex);
        }
    }

    protected virtual void CleanupResources()
    {
        try
        {
            _audioBuffer?.Clear();
            _currentSpeechStream?.Dispose();
            _preBuffer?.Clear();
            _rmsHistory?.Clear();
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

    protected virtual byte[] GetAndClearAudioData()
    {
        lock (_lock)
        {
            var data = _audioBuffer.ReadAll();
            _audioBuffer.Clear();
            return data;
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

    
}