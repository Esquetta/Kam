using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;
using System.Buffers;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

public abstract class VoiceRecognitionServiceBase : IVoiceRecognitionService
{
    protected CircularAudioBuffer _audioBuffer;
    protected bool _isListening;
    protected bool _disposed;
    protected readonly object _lock = new();

    protected readonly int _bufferCapacitySeconds = 30;

    // Ses algılama parametreleri
    protected readonly double _voiceThreshold = 0.005;
    protected readonly double _noiseFloor = 0.001;
    protected readonly int _silenceTimeoutMs = 1000;
    protected readonly int _minSpeechLengthMs = 300;
    protected readonly int _preBufferMs = 200;
    protected readonly int _postBufferMs = 300;

    // Algılama değişkenleri
    private DateTime _lastVoiceDetectedTime;
    private DateTime _firstVoiceDetectedTime;
    private PooledMemoryStream _currentSpeechStream;
    private CircularByteBuffer _preBuffer;
    private Queue<double> _rmsHistory;
    private readonly int _rmsHistorySize = 10;
    private readonly int _preBufferSize = 20;
    private double _adaptiveThreshold;
    private double _backgroundNoiseLevel;
    private int _voiceFrameCount;
    private int _silenceFrameCount;
    private readonly int _minVoiceFrames = 3;

    // ArrayPool for buffer recycling - internal so nested classes can access
    internal static readonly ArrayPool<byte> ByteArrayPool = ArrayPool<byte>.Shared;

    protected VoiceRecognitionServiceBase()
    {
        _audioBuffer = new CircularAudioBuffer(_bufferCapacitySeconds);
        _preBuffer = new CircularByteBuffer(_preBufferSize);
        _rmsHistory = new Queue<double>(_rmsHistorySize);
        _adaptiveThreshold = _voiceThreshold;
        _backgroundNoiseLevel = _noiseFloor;
        _currentSpeechStream = new PooledMemoryStream();
    }

    public event EventHandler<byte[]> OnVoiceCaptured;
    public event EventHandler<Exception> OnError;
    public event EventHandler OnListeningStarted;
    public event EventHandler OnListeningStopped;

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
            _currentSpeechStream?.Dispose();
            _currentSpeechStream = new PooledMemoryStream();
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

        // Hesaplamaları lock dışında yaparak contention'ı azalt
        var rms = CalculateRms(data);
        var energy = CalculateEnergy(data);

        lock (_lock)
        {
            _audioBuffer.Write(data);

            UpdateBackgroundNoise(rms);
            UpdateAdaptiveThreshold(rms);

            var isVoiceDetected = DetectVoice(rms, energy);

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
        int sampleCount = buffer.Length / 2;

        // Span<byte> kullanarak daha hızlı erişim
        ReadOnlySpan<byte> span = buffer.AsSpan();
        
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(span[i * 2] | (span[i * 2 + 1] << 8));
            double normalizedSample = sample / 32768.0;
            sumSquares += normalizedSample * normalizedSample;
        }

        return sampleCount > 0 ? Math.Sqrt(sumSquares / sampleCount) : 0;
    }

    private double CalculateEnergy(byte[] buffer)
    {
        if (buffer.Length == 0) return 0;

        double totalEnergy = 0;
        int sampleCount = buffer.Length / 2;

        ReadOnlySpan<byte> span = buffer.AsSpan();
        
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(span[i * 2] | (span[i * 2 + 1] << 8));
            totalEnergy += Math.Abs(sample);
        }

        return sampleCount > 0 ? totalEnergy / sampleCount : 0;
    }

    private void UpdateBackgroundNoise(double currentRms)
    {
        const double alpha = 0.05;
        _backgroundNoiseLevel = alpha * currentRms + (1 - alpha) * _backgroundNoiseLevel;
    }

    private void UpdateAdaptiveThreshold(double currentRms)
    {
        _rmsHistory.Enqueue(currentRms);
        if (_rmsHistory.Count > _rmsHistorySize)
        {
            _rmsHistory.Dequeue();
        }

        if (_rmsHistory.Count >= 5)
        {
            var averageRms = CalculateAverage(_rmsHistory);
            var maxRms = CalculateMax(_rmsHistory);

            _adaptiveThreshold = Math.Max(
                _voiceThreshold,
                Math.Max(_backgroundNoiseLevel * 2.5, averageRms * 1.5)
            );

            _adaptiveThreshold = Math.Min(_adaptiveThreshold, 0.1);
        }
    }

    private static double CalculateAverage(Queue<double> values)
    {
        double sum = 0;
        foreach (var value in values)
        {
            sum += value;
        }
        return sum / values.Count;
    }

    private static double CalculateMax(Queue<double> values)
    {
        double max = double.MinValue;
        foreach (var value in values)
        {
            if (value > max) max = value;
        }
        return max;
    }

    private bool DetectVoice(double rms, double energy)
    {
        var rmsCondition = rms > _adaptiveThreshold;
        var energyCondition = energy > _backgroundNoiseLevel * 1000;
        var relativeCondition = rms > _backgroundNoiseLevel * 3;

        if (rmsCondition || energyCondition || relativeCondition)
        {
            Debug.WriteLine($"Voice Detection - RMS: {rms:F6}, Threshold: {_adaptiveThreshold:F6}, Energy: {energy:F0}, Background: {_backgroundNoiseLevel:F6}");
        }

        return rmsCondition || energyCondition || relativeCondition;
    }

    private void ManagePreBuffer(byte[] data)
    {
        // ArrayPool kullanarak kopyalama - GC pressure azaltma
        _preBuffer.Enqueue(data);
    }

    private void HandleVoiceDetection(byte[] data, double rms)
    {
        var currentTime = DateTime.UtcNow;

        if (_currentSpeechStream?.Length == 0)
        {
            _firstVoiceDetectedTime = currentTime;

            foreach (var preData in _preBuffer.GetAll())
            {
                _currentSpeechStream.Write(preData, 0, preData.Length);
            }

            Debug.WriteLine($"Voice started - RMS: {rms:F6}");
        }

        _currentSpeechStream?.Write(data, 0, data.Length);
        _lastVoiceDetectedTime = currentTime;
        _voiceFrameCount++;
        _silenceFrameCount = 0;
    }

    private void HandleSilenceDetection(byte[] data)
    {
        if (_currentSpeechStream?.Length > 0)
        {
            _silenceFrameCount++;

            var now = DateTime.UtcNow;
            var silenceDuration = (now - _lastVoiceDetectedTime).TotalMilliseconds;
            var speechDuration = (now - _firstVoiceDetectedTime).TotalMilliseconds;

            if (silenceDuration < _postBufferMs)
            {
                _currentSpeechStream?.Write(data, 0, data.Length);
            }

            if (silenceDuration > _silenceTimeoutMs && _voiceFrameCount >= _minVoiceFrames)
            {
                if (speechDuration > _minSpeechLengthMs)
                {
                    var voiceData = _currentSpeechStream?.ToArray();

                    if (voiceData != null)
                    {
                        Debug.WriteLine($"Voice captured - Duration: {speechDuration}ms, Size: {voiceData.Length} bytes");
                        InvokeOnVoiceCaptured(voiceData);
                    }
                }
                else
                {
                    Debug.WriteLine($"Voice too short - Duration: {speechDuration}ms");
                }

                _currentSpeechStream?.Dispose();
                _currentSpeechStream = new PooledMemoryStream();
                _voiceFrameCount = 0;
                _silenceFrameCount = 0;
            }
        }
    }

    protected virtual void CleanupResources()
    {
        try
        {
            _audioBuffer?.Clear();
            _currentSpeechStream?.Dispose();
            _currentSpeechStream = null;
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

/// <summary>
/// Circular buffer for byte arrays using ArrayPool to minimize GC pressure
/// </summary>
internal sealed class CircularByteBuffer : IDisposable
{
    private readonly int _capacity;
    private readonly byte[][] _buffers;
    private readonly int[] _lengths;
    private int _head;
    private int _count;

    public CircularByteBuffer(int capacity)
    {
        _capacity = capacity;
        _buffers = new byte[capacity][];
        _lengths = new int[capacity];
    }

    public void Enqueue(byte[] data)
    {
        if (data == null || data.Length == 0) return;

        int index = _count < _capacity ? _count : _head;

        // Eski buffer'ı pool'a geri ver
        if (_buffers[index] != null && _buffers[index].Length < data.Length)
        {
            VoiceRecognitionServiceBase.ByteArrayPool.Return(_buffers[index]);
            _buffers[index] = null;
        }

        // Yeni buffer al ve kopyala
        if (_buffers[index] == null || _buffers[index].Length < data.Length)
        {
            _buffers[index] = VoiceRecognitionServiceBase.ByteArrayPool.Rent(data.Length);
        }

        Buffer.BlockCopy(data, 0, _buffers[index], 0, data.Length);
        _lengths[index] = data.Length;

        if (_count < _capacity)
        {
            _count++;
        }
        else
        {
            _head = (_head + 1) % _capacity;
        }
    }

    public IEnumerable<byte[]> GetAll()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = (_head + i) % _capacity;
            if (_buffers[index] != null)
            {
                var result = new byte[_lengths[index]];
                Buffer.BlockCopy(_buffers[index], 0, result, 0, _lengths[index]);
                yield return result;
            }
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _capacity; i++)
        {
            if (_buffers[i] != null)
            {
                VoiceRecognitionServiceBase.ByteArrayPool.Return(_buffers[i]);
                _buffers[i] = null;
            }
        }
        _head = 0;
        _count = 0;
    }

    public void Dispose()
    {
        Clear();
    }
}

/// <summary>
/// MemoryStream that uses ArrayPool for buffer management
/// </summary>
internal sealed class PooledMemoryStream : Stream
{
    private byte[] _buffer;
    private int _position;
    private int _length;
    private bool _disposed;

    public PooledMemoryStream()
    {
        _buffer = VoiceRecognitionServiceBase.ByteArrayPool.Rent(4096);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = (int)value;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(_position + count);
        Buffer.BlockCopy(buffer, offset, _buffer, _position, count);
        _position += count;
        if (_position > _length) _length = _position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int available = _length - _position;
        if (available <= 0) return 0;
        int toRead = Math.Min(count, available);
        Buffer.BlockCopy(_buffer, _position, buffer, offset, toRead);
        _position += toRead;
        return toRead;
    }

    public byte[] ToArray()
    {
        var result = new byte[_length];
        Buffer.BlockCopy(_buffer, 0, result, 0, _length);
        return result;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => (int)offset,
            SeekOrigin.Current => _position + (int)offset,
            SeekOrigin.End => _length + (int)offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void SetLength(long value)
    {
        EnsureCapacity((int)value);
        _length = (int)value;
    }

    private void EnsureCapacity(int capacity)
    {
        if (capacity <= _buffer.Length) return;

        int newCapacity = Math.Max(_buffer.Length * 2, capacity);
        var newBuffer = VoiceRecognitionServiceBase.ByteArrayPool.Rent(newCapacity);
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _length);
        VoiceRecognitionServiceBase.ByteArrayPool.Return(_buffer);
        _buffer = newBuffer;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (_buffer != null)
            {
                VoiceRecognitionServiceBase.ByteArrayPool.Return(_buffer);
                _buffer = null;
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
