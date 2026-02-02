using NAudio.Wave;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Service for testing microphone - records and provides feedback
/// Similar to Discord's "Let's Check" mic test feature
/// </summary>
public class VoiceTestService : IDisposable
{
    private readonly IVoiceRecognitionFactory _voiceRecognitionFactory;
    private IVoiceRecognitionService? _voiceRecognition;
    private readonly CircularAudioBuffer _testAudioBuffer;
    private bool _isRecording = false;
    private bool _isPlaying = false;
    private CancellationTokenSource? _levelMonitorCts;
    private string? _selectedDeviceId;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _waveProvider;

    /// <summary>
    /// Event fired when microphone input level changes (for VU meter)
    /// Value ranges from 0.0 to 1.0
    /// </summary>
    public event EventHandler<float>? OnInputLevelChanged;

    /// <summary>
    /// Event fired when recording state changes
    /// </summary>
    public event EventHandler<bool>? OnRecordingStateChanged;

    /// <summary>
    /// Event fired when playback state changes
    /// </summary>
    public event EventHandler<bool>? OnPlaybackStateChanged;

    /// <summary>
    /// Event fired when a test recording is completed
    /// </summary>
    public event EventHandler<byte[]>? OnRecordingCompleted;

    /// <summary>
    /// Gets whether the service is currently recording
    /// </summary>
    public bool IsRecording => _isRecording;

    /// <summary>
    /// Gets whether the service is currently playing back
    /// </summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// Gets the recorded audio data
    /// </summary>
    public byte[]? RecordedAudio { get; private set; }

    public VoiceTestService(IVoiceRecognitionFactory voiceRecognitionFactory)
    {
        _voiceRecognitionFactory = voiceRecognitionFactory;
        _testAudioBuffer = CircularAudioBuffer.ForAudio(10, 16000, 1, 16); // 10 seconds max
    }

    /// <summary>
    /// Sets the input device to use for recording
    /// </summary>
    public void SetInputDevice(string deviceId)
    {
        _selectedDeviceId = deviceId;
        // Note: The voice recognition service handles device selection internally
    }

    /// <summary>
    /// Starts recording audio from the microphone
    /// </summary>
    public void StartRecording(int maxDurationSeconds = 10)
    {
        if (_isRecording)
            return;

        try
        {
            // Clear previous recording
            _testAudioBuffer.Clear();
            RecordedAudio = null;

            // Create voice recognition service for recording
            _voiceRecognition = _voiceRecognitionFactory.Create();
            _voiceRecognition.OnVoiceCaptured += OnVoiceCaptured;

            // Start recording
            _voiceRecognition.StartListening();
            _isRecording = true;
            OnRecordingStateChanged?.Invoke(this, true);

            // Auto-stop after max duration
            Task.Run(async () =>
            {
                await Task.Delay(maxDurationSeconds * 1000);
                if (_isRecording)
                {
                    StopRecording();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start recording: {ex.Message}");
            StopRecording();
        }
    }

    /// <summary>
    /// Stops recording
    /// </summary>
    public void StopRecording()
    {
        if (!_isRecording)
            return;

        try
        {
            _voiceRecognition?.StopListening();
            _voiceRecognition?.Dispose();
            _voiceRecognition = null;
        }
        catch { }

        _isRecording = false;
        OnRecordingStateChanged?.Invoke(this, false);

        // Get recorded audio
        RecordedAudio = _testAudioBuffer.ReadAll();
        OnRecordingCompleted?.Invoke(this, RecordedAudio);
    }

    /// <summary>
    /// Starts playback of the recorded audio
    /// </summary>
    public void StartPlayback()
    {
        if (_isPlaying || RecordedAudio == null || RecordedAudio.Length == 0)
            return;

        try
        {
            _waveOut = new WaveOutEvent();
            _waveProvider = new BufferedWaveProvider(new WaveFormat(16000, 16, 1));
            _waveProvider.AddSamples(RecordedAudio, 0, RecordedAudio.Length);
            _waveProvider.BufferLength = RecordedAudio.Length + 1024;
            
            _waveOut.PlaybackStopped += (s, e) =>
            {
                _isPlaying = false;
                OnPlaybackStateChanged?.Invoke(this, false);
                _waveOut?.Dispose();
                _waveOut = null;
            };

            _waveOut.Init(_waveProvider);
            _waveOut.Play();
            _isPlaying = true;
            OnPlaybackStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start playback: {ex.Message}");
            _isPlaying = false;
            OnPlaybackStateChanged?.Invoke(this, false);
        }
    }

    /// <summary>
    /// Stops playback
    /// </summary>
    public void StopPlayback()
    {
        if (!_isPlaying)
            return;

        try
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
        }
        catch { }
        finally
        {
            _waveOut = null;
            _waveProvider = null;
            _isPlaying = false;
            OnPlaybackStateChanged?.Invoke(this, false);
        }
    }

    private void OnVoiceCaptured(object? sender, byte[] audioData)
    {
        // Store audio in buffer
        _testAudioBuffer.Write(audioData);

        // Calculate level for VU meter
        float level = CalculateAudioLevel(audioData);
        OnInputLevelChanged?.Invoke(this, level);
    }

    /// <summary>
    /// Starts monitoring input levels for visualization
    /// </summary>
    public void StartLevelMonitoring(CancellationToken cancellationToken = default)
    {
        _levelMonitorCts?.Cancel();
        _levelMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task.Run(async () =>
        {
            while (!_levelMonitorCts.Token.IsCancellationRequested)
            {
                // Random small fluctuation for visual effect when idle
                if (!_isRecording)
                {
                    OnInputLevelChanged?.Invoke(this, 0);
                }
                
                await Task.Delay(50, _levelMonitorCts.Token); // 20 FPS update rate
            }
        }, _levelMonitorCts.Token);
    }

    /// <summary>
    /// Stops monitoring input levels
    /// </summary>
    public void StopLevelMonitoring()
    {
        _levelMonitorCts?.Cancel();
        _levelMonitorCts = null;
    }

    private float CalculateAudioLevel(byte[] audioData)
    {
        if (audioData == null || audioData.Length < 2)
            return 0;

        // Calculate RMS (Root Mean Square) of 16-bit samples
        double sum = 0;
        int sampleCount = audioData.Length / 2;

        for (int i = 0; i < audioData.Length - 1; i += 2)
        {
            short sample = (short)(audioData[i] | (audioData[i + 1] << 8));
            double normalized = sample / 32768.0;
            sum += normalized * normalized;
        }

        double rms = Math.Sqrt(sum / sampleCount);
        return (float)rms;
    }

    public void Dispose()
    {
        StopRecording();
        StopPlayback();
        StopLevelMonitoring();
        _voiceRecognition?.Dispose();
        _waveOut?.Dispose();
    }
}
