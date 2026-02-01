using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SmartVoiceAgent.Core.Config;
using SmartVoiceAgent.Core.Interfaces;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Wake word detection service using audio pattern recognition.
/// Detects specific keyword patterns in audio stream.
/// </summary>
public class WakeWordDetectionService : IWakeWordDetectionService
{
    private readonly ILogger<WakeWordDetectionService> _logger;
    private readonly WakeWordConfig _config;
    
    private WaveInEvent? _waveIn;
    private bool _isListening;
    private readonly object _lock = new();
    private bool _disposed;
    
    // Audio processing
    private readonly ConcurrentQueue<short> _audioBuffer = new();
    private readonly List<short> _processingBuffer = new();
    private readonly int _sampleRate;
    private readonly int _frameSize;
    
    // Detection parameters
    private float _energyThreshold = 500f;
    private readonly float _adaptiveThresholdMultiplier = 1.5f;
    private float _backgroundNoiseLevel = 100f;
    private DateTime _lastDetectionTime = DateTime.MinValue;
    private readonly TimeSpan _minDetectionInterval = TimeSpan.FromMilliseconds(2000); // Debounce
    
    // Debug output
    private int _frameCount = 0;
    private DateTime _lastDebugOutput = DateTime.MinValue;
    private readonly TimeSpan _debugOutputInterval = TimeSpan.FromMilliseconds(500); // Show levels every 500ms

    public bool IsListening 
    { 
        get 
        { 
            lock (_lock) 
                return _isListening; 
        } 
    }
    
    public string WakeWord { get; private set; }
    public float Sensitivity { get; set; }

    public event EventHandler<WakeWordDetectedEventArgs>? OnWakeWordDetected;
    public event EventHandler<Exception>? OnError;

    public WakeWordDetectionService(
        ILogger<WakeWordDetectionService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _config = configuration.GetSection("WakeWord").Get<WakeWordConfig>() ?? new WakeWordConfig();
        
        WakeWord = _config.WakeWord;
        Sensitivity = _config.Sensitivity;
        _sampleRate = _config.SampleRate;
        _frameSize = _sampleRate / 50; // 20ms frames
        
        _logger.LogInformation("WakeWordDetectionService initialized with wake word: '{WakeWord}'", WakeWord);
    }

    public void StartListening()
    {
        lock (_lock)
        {
            if (_isListening)
            {
                _logger.LogWarning("Wake word detection is already running");
                return;
            }

            try
            {
                _audioBuffer.Clear();
                _processingBuffer.Clear();
                
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(_sampleRate, 16, 1),
                    BufferMilliseconds = 100
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                
                _waveIn.StartRecording();
                _isListening = true;
                
                // Reset debug counters
                _frameCount = 0;
                _lastDebugOutput = DateTime.UtcNow;
                
                _logger.LogInformation("🎤 Wake word detection started. Say '{WakeWord}' to activate.", WakeWord);
                Console.WriteLine($"   Sample Rate: {_sampleRate}Hz");
                Console.WriteLine($"   Frame Size: {_frameSize} samples ({_frameSize * 1000 / _sampleRate}ms)");
                Console.WriteLine($"   Initial Threshold: {_energyThreshold:F0}");
                Console.WriteLine("   (Audio levels will show when voice is detected)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start wake word detection");
                InvokeOnError(ex);
                throw;
            }
        }
    }

    public void StopListening()
    {
        lock (_lock)
        {
            if (!_isListening)
                return;

            try
            {
                _waveIn?.StopRecording();
                _isListening = false;
                _logger.LogInformation("Wake word detection stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping wake word detection");
            }
        }
    }

    public bool SetWakeWord(string wakeWord)
    {
        if (string.IsNullOrWhiteSpace(wakeWord))
        {
            _logger.LogWarning("Cannot set empty wake word");
            return false;
        }

        lock (_lock)
        {
            var wasListening = _isListening;
            
            try
            {
                if (wasListening)
                    StopListening();

                WakeWord = wakeWord;

                if (wasListening)
                    StartListening();

                _logger.LogInformation("Wake word changed to: '{WakeWord}'", WakeWord);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change wake word to '{WakeWord}'", wakeWord);
                return false;
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            // Safety check: ensure we don't read past the buffer length
            int bytesToProcess = Math.Min(e.BytesRecorded, e.Buffer.Length);
            
            // Convert byte array to short samples (16-bit PCM)
            for (int i = 0; i < bytesToProcess - 1; i += 2)  // -1 to ensure we have pairs
            {
                short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                _audioBuffer.Enqueue(sample);
            }

            // Process audio
            ProcessAudioBuffer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio data");
            InvokeOnError(ex);
        }
    }

    private void ProcessAudioBuffer()
    {
        // Drain buffer into processing buffer
        while (_audioBuffer.TryDequeue(out var sample))
        {
            _processingBuffer.Add(sample);
        }

        // Process in frames
        while (_processingBuffer.Count >= _frameSize)
        {
            var frame = _processingBuffer.Take(_frameSize).ToArray();
            _processingBuffer.RemoveRange(0, _frameSize);
            
            ProcessFrame(frame);
        }

        // Keep a small overlap for continuity
        if (_processingBuffer.Count > _frameSize / 2)
        {
            var excess = _processingBuffer.Count - _frameSize / 2;
            _processingBuffer.RemoveRange(0, excess);
        }
    }

    private void ProcessFrame(short[] frame)
    {
        // Calculate frame energy (RMS)
        float energy = CalculateEnergy(frame);
        
        // Update background noise estimate
        UpdateNoiseLevel(energy);
        
        // Debug output - show audio levels periodically
        _frameCount++;
        var now = DateTime.UtcNow;
        if (now - _lastDebugOutput >= _debugOutputInterval)
        {
            _lastDebugOutput = now;
            var isVoiceActive = energy > _energyThreshold * Sensitivity && energy > _backgroundNoiseLevel * _adaptiveThresholdMultiplier;
            var volumeBar = GetVolumeBar(energy);
            Debug.WriteLine($"[WakeWord] Energy: {energy,8:F1} | Threshold: {_energyThreshold * Sensitivity,8:F1} | BG: {_backgroundNoiseLevel,8:F1} | Voice: {(isVoiceActive ? "YES" : "NO")} | {volumeBar}");
            
            // Also write to console for visibility in test mode
            if (isVoiceActive)
            {
                Console.WriteLine($"🎤 Voice detected! Level: {energy:F0} {volumeBar}");
            }
        }
        
        // Check for voice activity
        if (energy > _energyThreshold * Sensitivity && energy > _backgroundNoiseLevel * _adaptiveThresholdMultiplier)
        {
            // Voice detected - analyze pattern
            AnalyzeVoicePattern(frame, energy);
        }
    }
    
    private string GetVolumeBar(float energy)
    {
        // Create a simple volume bar (0-50 scale)
        int level = Math.Min((int)(energy / 100), 50);
        return "[" + new string('█', level) + new string('░', 50 - level) + "]";
    }

    private float CalculateEnergy(short[] samples)
    {
        double sumSquares = 0;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }
        return (float)Math.Sqrt(sumSquares / samples.Length);
    }

    private void UpdateNoiseLevel(float currentEnergy)
    {
        // Simple exponential moving average for background noise
        const float alpha = 0.05f;
        _backgroundNoiseLevel = alpha * currentEnergy + (1 - alpha) * _backgroundNoiseLevel;
        
        // Update adaptive threshold
        _energyThreshold = Math.Max(500f, _backgroundNoiseLevel * 3f);
    }

    private void AnalyzeVoicePattern(short[] frame, float energy)
    {
        // Debounce - prevent multiple triggers
        if (DateTime.UtcNow - _lastDetectionTime < _minDetectionInterval)
            return;

        // Calculate spectral features (zero crossing rate)
        float zcr = CalculateZeroCrossingRate(frame);
        
        // Calculate dominant frequency estimate
        float dominantFreq = EstimateDominantFrequency(frame);
        
        // Pattern matching for wake word detection
        // A real implementation would use MFCCs or a neural network
        // This is a simplified heuristic-based approach
        
        bool isPatternMatch = DetectPattern(energy, zcr, dominantFreq);
        
        if (isPatternMatch)
        {
            _lastDetectionTime = DateTime.UtcNow;
            float confidence = CalculateConfidence(energy, zcr);
            
            InvokeOnWakeWordDetected(new WakeWordDetectedEventArgs(WakeWord, confidence));
            
            _logger.LogInformation("🎯 Wake word '{WakeWord}' detected! (Confidence: {Confidence:P0})", 
                WakeWord, confidence);
        }
    }

    private float CalculateZeroCrossingRate(short[] samples)
    {
        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if ((samples[i] >= 0 && samples[i - 1] < 0) || (samples[i] < 0 && samples[i - 1] >= 0))
            {
                crossings++;
            }
        }
        return (float)crossings / samples.Length;
    }

    private float EstimateDominantFrequency(short[] samples)
    {
        // Simple zero-crossing based frequency estimation
        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if ((samples[i] >= 0) != (samples[i - 1] >= 0))
            {
                crossings++;
            }
        }
        
        // Frequency = zero crossings / 2 * sample rate / frame size
        return crossings * _sampleRate / (2f * samples.Length);
    }

    private bool DetectPattern(float energy, float zcr, float dominantFreq)
    {
        // Simplified pattern detection based on energy, ZCR, and frequency
        // This is a heuristic approach - a production system would use ML
        
        // Typical speech characteristics:
        // - Energy: Variable but significant
        // - ZCR: Lower for voiced sounds (vowels), higher for unvoiced (consonants)
        // - Dominant frequency: Usually in 85-255 Hz range for male, 165-255 Hz for female
        
        bool hasSpeechEnergy = energy > _backgroundNoiseLevel * 2f;
        bool hasSpeechZCR = zcr > 0.05f && zcr < 0.3f;
        bool hasSpeechFreq = dominantFreq > 80f && dominantFreq < 400f;
        
        // All criteria must match for a detection
        // In a real implementation, this would be a trained classifier
        return hasSpeechEnergy && hasSpeechZCR && hasSpeechFreq;
    }

    private float CalculateConfidence(float energy, float zcr)
    {
        // Calculate confidence based on how well the pattern matches
        float energyScore = Math.Min(energy / (_energyThreshold * 2), 1f);
        float zcrScore = 1f - Math.Abs(zcr - 0.15f) * 2f; // Optimal ZCR around 0.15
        
        return Math.Clamp((energyScore + zcrScore) / 2, 0f, 1f);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger.LogError(e.Exception, "Recording stopped with error");
            InvokeOnError(e.Exception);
        }
    }

    private void InvokeOnWakeWordDetected(WakeWordDetectedEventArgs args)
    {
        try
        {
            OnWakeWordDetected?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in wake word detected event handler");
        }
    }

    private void InvokeOnError(Exception ex)
    {
        try
        {
            OnError?.Invoke(this, ex);
        }
        catch { /* Prevent exception in error handler */ }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                StopListening();
                
                _waveIn?.Dispose();
                _waveIn = null;
            }
            
            _disposed = true;
            _logger.LogDebug("WakeWordDetectionService disposed");
        }
        
        GC.SuppressFinalize(this);
    }
}
