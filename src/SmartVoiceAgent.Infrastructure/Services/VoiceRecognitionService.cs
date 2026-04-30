using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using SmartVoiceAgent.Infrastructure.Services.Stt;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Production-ready voice recognition service that integrates audio capture with HuggingFace STT.
/// This service handles continuous listening, voice activity detection, and transcription.
/// </summary>
public class VoiceRecognitionService : IVoiceRecognitionService
{
    private readonly ILogger<VoiceRecognitionService> _logger;
    private readonly HuggingFaceSTTService _sttService;
    private readonly VoiceRecognitionServiceBase _audioCapture;
    
    private bool _disposedValue;
    private bool _isListening;
    private readonly object _lock = new();
    
    // Audio buffer for accumulating voice data
    private MemoryStream? _currentRecording;
    private DateTime _recordingStartTime;
    private readonly TimeSpan _maxRecordingDuration = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _minRecordingDuration = TimeSpan.FromMilliseconds(500);

    public bool IsRecording => _isListening && _currentRecording?.Length > 0;
    
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

    public event EventHandler<byte[]>? OnVoiceCaptured;
    public event EventHandler<Exception>? OnError;
    public event EventHandler? OnRecordingStarted;
    public event EventHandler? OnRecordingStopped;
    public event EventHandler? OnListeningStarted;
    public event EventHandler? OnListeningStopped;

    public VoiceRecognitionService(
        ILogger<VoiceRecognitionService> logger,
        HuggingFaceSTTService sttService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sttService = sttService ?? throw new ArgumentNullException(nameof(sttService));
        
        // Create platform-specific audio capture service
        _audioCapture = CreatePlatformAudioCapture();
        
        // Subscribe to audio capture events
        _audioCapture.OnVoiceCaptured += OnAudioVoiceCaptured;
        _audioCapture.OnError += OnAudioError;
        _audioCapture.OnListeningStarted += OnAudioListeningStarted;
        _audioCapture.OnListeningStopped += OnAudioListeningStopped;
    }

    private VoiceRecognitionServiceBase CreatePlatformAudioCapture()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsVoiceRecognitionService();
        if (OperatingSystem.IsMacOS())
            return new MacOSVoiceRecognitionService();
        if (OperatingSystem.IsLinux())
            return new LinuxVoiceRecognitionService();

        throw new PlatformNotSupportedException("Voice recognition is not supported on this platform.");
    }

    public void StartListening()
    {
        lock (_lock)
        {
            if (_isListening)
                throw new InvalidOperationException("Already listening.");

            if (_disposedValue)
                throw new ObjectDisposedException(nameof(VoiceRecognitionService));

            _currentRecording = new MemoryStream();
            _recordingStartTime = DateTime.UtcNow;
            _isListening = true;

            _logger.LogInformation("Starting voice recognition...");
            _audioCapture.StartListening();
        }
    }

    public void StopListening()
    {
        lock (_lock)
        {
            if (!_isListening)
                return;

            _audioCapture.StopListening();
            _isListening = false;

            // Process any remaining audio
            ProcessRemainingAudio();
        }
    }

    public void ClearBuffer()
    {
        lock (_lock)
        {
            _currentRecording?.Dispose();
            _currentRecording = new MemoryStream();
            _audioCapture.ClearBuffer();
        }
    }

    public long GetCurrentBufferSize()
    {
        lock (_lock)
        {
            return _currentRecording?.Length ?? 0;
        }
    }

    /// <summary>
    /// Listens for voice input and returns the transcribed text.
    /// This is the main API for voice recognition.
    /// </summary>
    public async Task<string> ListenAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<string>();
        
        EventHandler<byte[]>? voiceCapturedHandler = null;
        EventHandler<Exception>? errorHandler = null;
        
        voiceCapturedHandler = async (s, audioData) =>
        {
            OnVoiceCaptured -= voiceCapturedHandler;
            OnError -= errorHandler;
            
            try
            {
                var result = await TranscribeAudioAsync(audioData, cancellationToken);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };
        
        errorHandler = (s, ex) =>
        {
            OnVoiceCaptured -= voiceCapturedHandler;
            OnError -= errorHandler;
            tcs.TrySetException(ex);
        };

        OnVoiceCaptured += voiceCapturedHandler;
        OnError += errorHandler;

        try
        {
            StartListening();
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // Max listening time
            
            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            StopListening();
        }
    }

    /// <summary>
    /// Records audio for a specific duration.
    /// </summary>
    public async Task<byte[]> RecordForDurationAsync(TimeSpan duration)
    {
        var tcs = new TaskCompletionSource<byte[]>();
        
        EventHandler<byte[]>? voiceCapturedHandler = null;
        EventHandler<Exception>? errorHandler = null;
        
        voiceCapturedHandler = (s, data) =>
        {
            OnVoiceCaptured -= voiceCapturedHandler;
            OnError -= errorHandler;
            tcs.TrySetResult(data);
        };
        
        errorHandler = (s, ex) =>
        {
            OnVoiceCaptured -= voiceCapturedHandler;
            OnError -= errorHandler;
            tcs.TrySetException(ex);
        };

        OnVoiceCaptured += voiceCapturedHandler;
        OnError += errorHandler;

        try
        {
            StartListening();
            await Task.Delay(duration);
            StopListening();
            
            // Return accumulated audio if not already captured
            if (!tcs.Task.IsCompleted)
            {
                lock (_lock)
                {
                    var audioData = _currentRecording?.ToArray() ?? Array.Empty<byte>();
                    tcs.TrySetResult(audioData);
                }
            }
            
            return await tcs.Task;
        }
        finally
        {
            OnVoiceCaptured -= voiceCapturedHandler;
            OnError -= errorHandler;
        }
    }

    private async Task<string> TranscribeAudioAsync(byte[] audioData, CancellationToken cancellationToken)
    {
        if (audioData == null || audioData.Length == 0)
        {
            _logger.LogWarning("No audio data to transcribe");
            return string.Empty;
        }

        _logger.LogInformation("Transcribing {Length} bytes of audio using HuggingFace...", audioData.Length);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await _sttService.ConvertToTextAsync(audioData, cancellationToken);
            
            stopwatch.Stop();
            
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                _logger.LogError("Transcription error: {Error}", result.ErrorMessage);
                throw new Exception($"Transcription failed: {result.ErrorMessage}");
            }

            _logger.LogInformation(
                "Transcription completed in {ElapsedMs}ms: '{Text}' (Confidence: {Confidence:P})",
                stopwatch.ElapsedMilliseconds,
                result.Text,
                result.Confidence);

            return result.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private void OnAudioVoiceCaptured(object? sender, byte[] audioData)
    {
        try
        {
            lock (_lock)
            {
                if (_currentRecording != null && audioData?.Length > 0)
                {
                    _currentRecording.Write(audioData, 0, audioData.Length);
                }
            }
            
            // Forward the event to subscribers
            if (audioData != null)
            {
                OnVoiceCaptured?.Invoke(this, audioData);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing captured voice data");
            OnError?.Invoke(this, ex);
        }
    }

    private void OnAudioError(object? sender, Exception ex)
    {
        _logger.LogError(ex, "Audio capture error");
        OnError?.Invoke(this, ex);
    }

    private void OnAudioListeningStarted(object? sender, EventArgs e)
    {
        OnListeningStarted?.Invoke(this, e);
        OnRecordingStarted?.Invoke(this, e);
    }

    private void OnAudioListeningStopped(object? sender, EventArgs e)
    {
        OnRecordingStopped?.Invoke(this, e);
        OnListeningStopped?.Invoke(this, e);
    }

    private void ProcessRemainingAudio()
    {
        lock (_lock)
        {
            if (_currentRecording?.Length > 0)
            {
                var audioData = _currentRecording.ToArray();
                
                // Only process if recording is long enough
                var recordingDuration = DateTime.UtcNow - _recordingStartTime;
                if (recordingDuration >= _minRecordingDuration)
                {
                    _logger.LogDebug("Processing remaining audio: {Length} bytes", audioData.Length);
                    OnVoiceCaptured?.Invoke(this, audioData);
                }
                else
                {
                    _logger.LogDebug("Recording too short ({DurationMs}ms), discarding", recordingDuration.TotalMilliseconds);
                }
            }
            
            _currentRecording?.Dispose();
            _currentRecording = null;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _audioCapture.OnVoiceCaptured -= OnAudioVoiceCaptured;
                _audioCapture.OnError -= OnAudioError;
                _audioCapture.OnListeningStarted -= OnAudioListeningStarted;
                _audioCapture.OnListeningStopped -= OnAudioListeningStopped;
                
                _audioCapture?.Dispose();
                _currentRecording?.Dispose();
                _sttService?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
