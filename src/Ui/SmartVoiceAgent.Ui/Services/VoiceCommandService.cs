using Avalonia.Threading;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Service that handles voice commands in the UI - wake word detection, recording, and STT
/// </summary>
public class VoiceCommandService : IDisposable
{
    private readonly IWakeWordDetectionService _wakeWordService;
    private readonly IVoiceRecognitionFactory _voiceRecognitionFactory;
    private readonly IMultiSTTService _sttService;
    private readonly INoiseSuppressionService _noiseSuppressionService;
    private readonly ICommandInputService _commandInputService;
    private readonly IUiLogService _uiLogService;
    
    private IVoiceRecognitionService? _voiceRecognition;
    private CancellationTokenSource? _wakeWordCts;
    private bool _isRecording = false;
    private bool _isDisposed = false;

    // Events for UI updates
    public event EventHandler<VoiceStatusEventArgs>? StatusChanged;
    public event EventHandler<string>? OnTranscriptionResult;
    public event EventHandler<string>? OnError;

    public bool IsListeningForWakeWord => _wakeWordCts != null && !_wakeWordCts.IsCancellationRequested;
    public bool IsRecording => _isRecording;

    public VoiceCommandService(
        IWakeWordDetectionService wakeWordService,
        IVoiceRecognitionFactory voiceRecognitionFactory,
        IMultiSTTService sttService,
        INoiseSuppressionService noiseSuppressionService,
        ICommandInputService commandInputService,
        IUiLogService uiLogService)
    {
        _wakeWordService = wakeWordService;
        _voiceRecognitionFactory = voiceRecognitionFactory;
        _sttService = sttService;
        _noiseSuppressionService = noiseSuppressionService;
        _commandInputService = commandInputService;
        _uiLogService = uiLogService;

        // Subscribe to wake word events
        _wakeWordService.OnWakeWordDetected += OnWakeWordDetected;
        _wakeWordService.OnError += OnWakeWordErrorHandler;
    }

    /// <summary>
    /// Starts listening for the wake word ("Hey Kam")
    /// </summary>
    public void StartWakeWordDetection()
    {
        if (_isDisposed) return;
        
        try
        {
            _wakeWordCts = new CancellationTokenSource();
            _wakeWordService.StartListening();
            
            UpdateStatus(VoiceStatus.ListeningForWakeWord, "Say 'Hey Kam' to activate");
            _uiLogService.Log("🎤 Wake word detection started - Say 'Hey Kam'");
        }
        catch (Exception ex)
        {
            UpdateStatus(VoiceStatus.Error, $"Wake word error: {ex.Message}");
            _uiLogService.Log($"Failed to start wake word detection: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops wake word detection
    /// </summary>
    public void StopWakeWordDetection()
    {
        try
        {
            _wakeWordCts?.Cancel();
            _wakeWordService.StopListening();
            
            UpdateStatus(VoiceStatus.Idle, "Voice control inactive");
            _uiLogService.Log("🛑 Wake word detection stopped");
        }
        catch (Exception ex)
        {
            _uiLogService.Log($"Error stopping wake word detection: {ex.Message}");
        }
    }

    /// <summary>
    /// Toggle wake word detection on/off
    /// </summary>
    public void ToggleWakeWordDetection()
    {
        if (IsListeningForWakeWord)
        {
            StopWakeWordDetection();
        }
        else
        {
            StartWakeWordDetection();
        }
    }

    /// <summary>
    /// Manually start voice recording (alternative to wake word)
    /// </summary>
    public async Task StartVoiceRecordingAsync(bool useNoiseSuppression = true)
    {
        if (_isDisposed || _isRecording) return;

        try
        {
            _isRecording = true;
            UpdateStatus(VoiceStatus.Recording, "Recording... Speak now!");
            _uiLogService.Log("🔴 Recording started...");

            // Create voice recognition service
            _voiceRecognition = _voiceRecognitionFactory.Create();
            
            var tcs = new TaskCompletionSource<byte[]>();
            
            _voiceRecognition.OnVoiceCaptured += (s, data) =>
            {
                tcs.TrySetResult(data);
            };

            _voiceRecognition.StartListening();

            // Wait for voice capture with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            byte[] audioData;
            
            try
            {
                audioData = await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus(VoiceStatus.Idle, "Recording timeout");
                _uiLogService.Log("⏱️ Recording timeout");
                return;
            }
            finally
            {
                _voiceRecognition.StopListening();
            }

            // Apply noise suppression if requested
            if (useNoiseSuppression)
            {
                UpdateStatus(VoiceStatus.Processing, "Applying noise suppression...");
                audioData = _noiseSuppressionService.SuppressNoise(audioData);
            }

            // Transcribe audio
            await TranscribeAndSubmitAsync(audioData);
        }
        catch (Exception ex)
        {
            UpdateStatus(VoiceStatus.Error, $"Recording error: {ex.Message}");
            _uiLogService.Log($"Voice recording error: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
        finally
        {
            _isRecording = false;
        }
    }

    /// <summary>
    /// Stop current recording
    /// </summary>
    public void StopRecording()
    {
        if (_voiceRecognition != null)
        {
            _voiceRecognition.StopListening();
            _isRecording = false;
            UpdateStatus(VoiceStatus.Idle, "Recording stopped");
        }
    }

    /// <summary>
    /// Handle wake word detection event
    /// </summary>
    private void OnWakeWordDetected(object? sender, EventArgs e)
    {
        if (_isDisposed) return;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                UpdateStatus(VoiceStatus.WakeWordDetected, "🎯 Wake word detected!");
                _uiLogService.Log("🎯 Wake word 'Hey Kam' detected!");

                // Stop wake word detection while processing
                _wakeWordService.StopListening();

                // Start recording for command
                await StartVoiceRecordingAfterWakeWordAsync();

                // Resume wake word detection
                if (!_isDisposed && _wakeWordCts?.IsCancellationRequested == false)
                {
                    _wakeWordService.StartListening();
                    UpdateStatus(VoiceStatus.ListeningForWakeWord, "Say 'Hey Kam' to activate");
                }
            }
            catch (Exception ex)
            {
                _uiLogService.Log($"Error handling wake word: {ex.Message}");
                UpdateStatus(VoiceStatus.Error, $"Wake word handling error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Recording after wake word detection
    /// </summary>
    private async Task StartVoiceRecordingAfterWakeWordAsync()
    {
        try
        {
            _isRecording = true;
            UpdateStatus(VoiceStatus.Recording, "Recording command...");
            _uiLogService.Log("🔴 Recording command...");

            // Create voice recognition service
            _voiceRecognition = _voiceRecognitionFactory.Create();
            
            var tcs = new TaskCompletionSource<byte[]>();
            
            _voiceRecognition.OnVoiceCaptured += (s, data) =>
            {
                tcs.TrySetResult(data);
            };

            _voiceRecognition.StartListening();

            // Wait for voice capture with shorter timeout after wake word
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            byte[] audioData;
            
            try
            {
                audioData = await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus(VoiceStatus.ListeningForWakeWord, "No command heard");
                _uiLogService.Log("⏱️ No command detected after wake word");
                return;
            }
            finally
            {
                _voiceRecognition.StopListening();
                _isRecording = false;
            }

            // Apply noise suppression
            UpdateStatus(VoiceStatus.Processing, "Processing audio...");
            audioData = _noiseSuppressionService.SuppressNoise(audioData);

            // Transcribe and submit
            await TranscribeAndSubmitAsync(audioData);
        }
        catch (Exception ex)
        {
            UpdateStatus(VoiceStatus.Error, $"Recording error: {ex.Message}");
            _uiLogService.Log($"Post-wake-word recording error: {ex.Message}");
        }
    }

    /// <summary>
    /// Transcribe audio and submit as command
    /// </summary>
    private async Task TranscribeAndSubmitAsync(byte[] audioData)
    {
        try
        {
            UpdateStatus(VoiceStatus.Transcribing, "Transcribing...");
            _uiLogService.Log("📝 Transcribing audio...");

            var result = await _sttService.ConvertToTextAsync(audioData);

            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                UpdateStatus(VoiceStatus.CommandRecognized, $"Command: {result.Text}");
                _uiLogService.Log($"🎤 Voice command: '{result.Text}' (Confidence: {result.Confidence:P0})");
                
                OnTranscriptionResult?.Invoke(this, result.Text);
                
                // Submit to command input service
                _commandInputService.SubmitCommand(result.Text);
            }
            else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                UpdateStatus(VoiceStatus.Error, $"Transcription error: {result.ErrorMessage}");
                _uiLogService.Log($"Transcription error: {result.ErrorMessage}");
            }
            else
            {
                UpdateStatus(VoiceStatus.Idle, "No speech detected");
                _uiLogService.Log("🤷 No speech detected in audio");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus(VoiceStatus.Error, $"Transcription error: {ex.Message}");
            _uiLogService.Log($"Transcription error: {ex.Message}");
        }
    }

    private void OnWakeWordErrorHandler(object? sender, Exception ex)
    {
        var error = ex.Message;
        UpdateStatus(VoiceStatus.Error, $"Wake word error: {error}");
        _uiLogService.Log($"Wake word detection error: {error}", LogLevel.Error);
        OnError?.Invoke(this, error);
    }

    private void UpdateStatus(VoiceStatus status, string message)
    {
        StatusChanged?.Invoke(this, new VoiceStatusEventArgs(status, message));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopWakeWordDetection();
        StopRecording();

        _wakeWordService.OnWakeWordDetected -= OnWakeWordDetected;
        _wakeWordService.OnError -= OnWakeWordErrorHandler;

        _wakeWordCts?.Dispose();
        _voiceRecognition?.Dispose();
    }
}

/// <summary>
/// Voice service status enumeration
/// </summary>
public enum VoiceStatus
{
    Idle,
    ListeningForWakeWord,
    WakeWordDetected,
    Recording,
    Processing,
    Transcribing,
    CommandRecognized,
    Error
}

/// <summary>
/// Event args for voice status changes
/// </summary>
public class VoiceStatusEventArgs : EventArgs
{
    public VoiceStatus Status { get; }
    public string Message { get; }

    public VoiceStatusEventArgs(VoiceStatus status, string message)
    {
        Status = status;
        Message = message;
    }
}
