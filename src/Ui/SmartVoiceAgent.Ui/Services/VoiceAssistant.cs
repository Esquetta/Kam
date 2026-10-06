using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Infrastructure.Services.Speech;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// What the voice assistant is doing.
/// </summary>
public enum VoiceState
{
    /// <summary>Voice isn't available on this computer.</summary>
    Off,

    /// <summary>Waiting for the talk button or shortcut.</summary>
    Ready,

    /// <summary>Waiting for the wake phrase.</summary>
    WakeListening,

    /// <summary>Downloading a speech model.</summary>
    Preparing,

    /// <summary>Recording a command.</summary>
    Listening,

    /// <summary>Turning the recording into text.</summary>
    Transcribing,

    /// <summary>Reading a reply aloud.</summary>
    Speaking
}

/// <summary>
/// Why the last voice command didn't go through.
/// </summary>
public enum VoiceProblem
{
    /// <summary>Nothing went wrong.</summary>
    None,

    /// <summary>The microphone couldn't be opened or stopped working.</summary>
    MicrophoneUnavailable,

    /// <summary>A speech model couldn't be downloaded.</summary>
    ModelDownloadFailed,

    /// <summary>No speech engine is set up.</summary>
    NoSpeechEngine,

    /// <summary>The speech engine failed.</summary>
    TranscriptionFailed,

    /// <summary>No words were heard.</summary>
    NothingHeard,

    /// <summary>The wake phrase listener couldn't start.</summary>
    WakeWordUnavailable
}

/// <summary>
/// Describes a change of <see cref="VoiceAssistant.State"/>.
/// </summary>
public sealed class VoiceStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event data.
    /// </summary>
    public VoiceStateChangedEventArgs(VoiceState state, VoiceProblem problem = VoiceProblem.None, double? progress = null, string? detail = null)
    {
        State = state;
        Problem = problem;
        Progress = progress;
        Detail = detail;
    }

    /// <summary>Gets the new state.</summary>
    public VoiceState State { get; }

    /// <summary>Gets what went wrong just before, if anything.</summary>
    public VoiceProblem Problem { get; }

    /// <summary>Gets the download progress from 0 to 1 while <see cref="VoiceState.Preparing"/>.</summary>
    public double? Progress { get; }

    /// <summary>Gets technical detail for the activity log.</summary>
    public string? Detail { get; }
}

/// <summary>
/// Runs voice: the talk button and shortcut start and stop recording, the wake phrase starts it hands-free,
/// commands go to the agent chat, and replies can be read aloud. Raises its events on background threads.
/// </summary>
public sealed class VoiceAssistant : IDisposable
{
    private static readonly TimeSpan NoSpeechTimeout = TimeSpan.FromSeconds(8);

    private readonly IVoiceRecognitionFactory _recorders;
    private readonly IMultiSTTService _speechToText;
    private readonly IWakeWordDetectionService _wakeWord;
    private readonly ISpeechModelStore _models;
    private readonly ITextToSpeechService _speech;
    private readonly INoiseSuppressionService? _noiseSuppression;
    private readonly IConfiguration _configuration;
    private readonly IUiLogService? _log;
    private readonly object _gate = new();
    private IVoiceRecognitionService? _recorder;
    private CancellationTokenSource? _listenTimeout;
    private bool _heardSpeech;
    private bool _wakeWordEnabled;
    private bool _disposed;

    /// <summary>
    /// Creates the assistant.
    /// </summary>
    public VoiceAssistant(
        IVoiceRecognitionFactory recorders,
        IMultiSTTService speechToText,
        IWakeWordDetectionService wakeWord,
        ISpeechModelStore models,
        ITextToSpeechService speech,
        IConfiguration configuration,
        INoiseSuppressionService? noiseSuppression = null,
        IUiLogService? log = null)
    {
        _recorders = recorders;
        _speechToText = speechToText;
        _wakeWord = wakeWord;
        _models = models;
        _speech = speech;
        _configuration = configuration;
        _noiseSuppression = noiseSuppression;
        _log = log;
        _wakeWord.OnWakeWordDetected += OnWakeWordDetected;
        _wakeWord.OnError += OnWakeWordError;
    }

    /// <summary>Raised when <see cref="State"/> changes or a command fails.</summary>
    public event EventHandler<VoiceStateChangedEventArgs>? StateChanged;

    /// <summary>Raised with the microphone level, 0..1, while recording a command.</summary>
    public event EventHandler<float>? LevelChanged;

    /// <summary>Raised with the text of each recognized command.</summary>
    public event EventHandler<string>? CommandRecognized;

    /// <summary>
    /// Gets or sets what runs a recognized command, such as the agent chat; it returns <c>false</c> when it
    /// can't take the command.
    /// </summary>
    public Func<string, bool>? CommandRouter { get; set; }

    /// <summary>
    /// Gets or sets whether noise suppression runs on recordings before recognition.
    /// </summary>
    public bool UseNoiseSuppression { get; set; }

    /// <summary>Gets what the assistant is doing.</summary>
    public VoiceState State { get; private set; } = VoiceState.Ready;

    /// <summary>Gets whether the wake phrase listener is on.</summary>
    public bool IsWakeWordEnabled => _wakeWordEnabled;

    /// <summary>Gets whether replies can be read aloud on this computer.</summary>
    public bool CanSpeak => _speech.IsAvailable;

    /// <summary>
    /// Starts recording a command, or stops the recording and sends what was said. While a reply is being
    /// read, stops reading instead.
    /// </summary>
    public Task ToggleTalkAsync()
    {
        switch (State)
        {
            case VoiceState.Listening:
                StopRecording();
                return Task.CompletedTask;
            case VoiceState.Speaking:
                _speech.Stop();
                return Task.CompletedTask;
            case VoiceState.Preparing or VoiceState.Transcribing:
                return Task.CompletedTask;
            default:
                return BeginListeningAsync();
        }
    }

    /// <summary>
    /// Stops recording without sending, and stops reading aloud.
    /// </summary>
    public void Cancel()
    {
        IVoiceRecognitionService? recorder;
        lock (_gate)
        {
            recorder = _recorder;
            _recorder = null;
            _listenTimeout?.Cancel();
        }

        if (recorder is not null)
        {
            Detach(recorder);
            recorder.Dispose();
            Rest();
        }

        _speech.Stop();
    }

    /// <summary>
    /// Turns the wake phrase listener on or off. Downloads the small wake word model the first time.
    /// </summary>
    /// <param name="enabled">Whether to listen for the wake phrase.</param>
    public async Task SetWakeWordEnabledAsync(bool enabled)
    {
        _wakeWordEnabled = enabled;
        if (!enabled)
        {
            _wakeWord.StopListening();
            if (State == VoiceState.WakeListening)
            {
                SetState(VoiceState.Ready);
            }

            return;
        }

        if (!await EnsureModelAsync(SpeechModelCatalog.WakeWordModel).ConfigureAwait(false))
        {
            _wakeWordEnabled = false;
            SetState(VoiceState.Ready, VoiceProblem.WakeWordUnavailable);
            return;
        }

        if (State is VoiceState.Ready or VoiceState.Preparing)
        {
            Rest();
        }
    }

    /// <summary>
    /// Reads an agent reply aloud, without its code blocks, tables and link targets.
    /// </summary>
    /// <param name="markdown">The reply as the chat shows it.</param>
    public async Task SpeakAsync(string markdown)
    {
        var text = SpeechTextFormatter.Format(markdown);
        if (text.Length == 0 || !_speech.IsAvailable || State is VoiceState.Listening or VoiceState.Transcribing)
        {
            return;
        }

        _wakeWord.StopListening();
        SetState(VoiceState.Speaking);
        try
        {
            await _speech.SpeakAsync(text).ConfigureAwait(false);
        }
        finally
        {
            if (State == VoiceState.Speaking)
            {
                Rest();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();
        _wakeWord.OnWakeWordDetected -= OnWakeWordDetected;
        _wakeWord.OnError -= OnWakeWordError;
        _wakeWord.StopListening();
    }

    private async Task BeginListeningAsync()
    {
        if (!await EnsureSpeechEngineAsync().ConfigureAwait(false))
        {
            return;
        }

        _wakeWord.StopListening();
        _speech.Stop();

        IVoiceRecognitionService recorder;
        try
        {
            recorder = _recorders.Create();
            recorder.OnVoiceCaptured += OnCommandCaptured;
            recorder.OnAudioLevel += OnLevel;
            recorder.OnError += OnRecorderError;
            lock (_gate)
            {
                _recorder?.Dispose();
                _recorder = recorder;
                _heardSpeech = false;
            }

            recorder.StartListening();
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _recorder = null;
            }

            Rest(VoiceProblem.MicrophoneUnavailable, ex.Message);
            return;
        }

        SetState(VoiceState.Listening);
        var timeout = new CancellationTokenSource();
        lock (_gate)
        {
            _listenTimeout?.Cancel();
            _listenTimeout = timeout;
        }

        _ = StopIfSilentAsync(recorder, timeout.Token);
    }

    private async Task StopIfSilentAsync(IVoiceRecognitionService recorder, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(NoSpeechTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool silent;
        lock (_gate)
        {
            silent = ReferenceEquals(_recorder, recorder) && !_heardSpeech;
        }

        if (silent)
        {
            StopRecording();
        }
    }

    private void StopRecording()
    {
        IVoiceRecognitionService? recorder;
        lock (_gate)
        {
            recorder = _recorder;
        }

        // Stopping delivers speech still in progress through OnCommandCaptured.
        recorder?.StopListening();

        bool nothing;
        lock (_gate)
        {
            nothing = ReferenceEquals(_recorder, recorder) && recorder is not null;
            if (nothing)
            {
                _recorder = null;
            }
        }

        if (nothing)
        {
            Detach(recorder!);
            Rest(VoiceProblem.NothingHeard);
        }
    }

    private void OnLevel(object? sender, float level)
    {
        if (level > 0.02f)
        {
            lock (_gate)
            {
                _heardSpeech = true;
            }
        }

        LevelChanged?.Invoke(this, level);
    }

    private void OnCommandCaptured(object? sender, byte[] audio)
    {
        IVoiceRecognitionService? recorder;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _recorder))
            {
                return;
            }

            recorder = _recorder;
            _recorder = null;
            _listenTimeout?.Cancel();
        }

        // The first utterance is the command; stop recording and transcribe it.
        Detach(recorder!);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            recorder!.Dispose();
            _ = TranscribeAndRouteAsync(audio, wakePhraseFirst: false);
        });
    }

    private void OnRecorderError(object? sender, Exception error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _recorder))
            {
                return;
            }

            _recorder = null;
        }

        if (sender is IVoiceRecognitionService recorder)
        {
            Detach(recorder);
            ThreadPool.QueueUserWorkItem(_ => recorder.Dispose());
        }

        Rest(VoiceProblem.MicrophoneUnavailable, error.Message);
    }

    private void Detach(IVoiceRecognitionService recorder)
    {
        recorder.OnVoiceCaptured -= OnCommandCaptured;
        recorder.OnAudioLevel -= OnLevel;
        recorder.OnError -= OnRecorderError;
    }

    private async Task TranscribeAndRouteAsync(byte[] audio, bool wakePhraseFirst)
    {
        SetState(VoiceState.Transcribing);
        try
        {
            if (UseNoiseSuppression && _noiseSuppression is not null)
            {
                audio = _noiseSuppression.SuppressNoise(audio, new NoiseSuppressionOptions { SuppressionStrength = 0.5f });
            }

            var result = await _speechToText.ConvertToTextAsync(audio).ConfigureAwait(false);
            var text = result.Text?.Trim() ?? string.Empty;
            if (wakePhraseFirst && text.Length > 0)
            {
                var match = WakePhraseMatcher.Match(text, _wakeWord.WakeWord, 0.5);
                if (match.IsMatch)
                {
                    text = match.FollowingText;
                }
            }

            if (text.Length == 0)
            {
                var noSpeech = string.IsNullOrEmpty(result.ErrorMessage) || result.ErrorMessage == TranscriptCleaner.NoSpeechMessage;
                if (wakePhraseFirst && noSpeech)
                {
                    // Only the wake phrase was clear; ask for the command.
                    await BeginListeningAsync().ConfigureAwait(false);
                    return;
                }

                Rest(noSpeech ? VoiceProblem.NothingHeard : VoiceProblem.TranscriptionFailed, result.ErrorMessage);
                return;
            }

            _log?.Log($"Voice command recognized with {result.UsedProvider} in {result.TotalProcessingTime.TotalMilliseconds:F0} ms");
            CommandRecognized?.Invoke(this, text);
            CommandRouter?.Invoke(text);
            Rest();
        }
        catch (Exception ex)
        {
            Rest(VoiceProblem.TranscriptionFailed, ex.Message);
        }
    }

    private void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
    {
        if (State != VoiceState.WakeListening || _disposed)
        {
            return;
        }

        _wakeWord.StopListening();
        _log?.Log($"Wake phrase heard ({e.Confidence:P0})");
        if (!string.IsNullOrWhiteSpace(e.FollowingText) && e.Utterance.Length > 0)
        {
            // "Hey Kam, open Spotify": transcribe the whole utterance with the main engine.
            _ = TranscribeAndRouteAsync(e.Utterance, wakePhraseFirst: true);
        }
        else
        {
            _ = BeginListeningAsync();
        }
    }

    private void OnWakeWordError(object? sender, Exception error)
    {
        _log?.Log($"Wake phrase listener failed: {error.Message}", LogLevel.Warning);
        if (State == VoiceState.WakeListening)
        {
            _wakeWordEnabled = false;
            _wakeWord.StopListening();
            SetState(VoiceState.Ready, VoiceProblem.WakeWordUnavailable, detail: error.Message);
        }
    }

    private async Task<bool> EnsureSpeechEngineAsync()
    {
        var settings = VoiceSettings.Read(_configuration);
        if (settings.UsesApi)
        {
            if (settings.HasApi || _models.IsDownloaded(settings.LocalModel))
            {
                return true;
            }

            Rest(VoiceProblem.NoSpeechEngine);
            return false;
        }

        if (settings.LocalModelPath is not null)
        {
            return true;
        }

        return await EnsureModelAsync(settings.LocalModel).ConfigureAwait(false);
    }

    private async Task<bool> EnsureModelAsync(string model)
    {
        if (_models.IsDownloaded(model))
        {
            return true;
        }

        var resting = State;
        SetState(VoiceState.Preparing, progress: 0);
        try
        {
            await _models.EnsureModelAsync(model, new SynchronousProgress(value => SetState(VoiceState.Preparing, progress: value)))
                .ConfigureAwait(false);
            SetState(resting == VoiceState.Preparing ? VoiceState.Ready : resting);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Log($"Speech model {model} download failed: {ex.Message}", LogLevel.Warning);
            SetState(VoiceState.Ready, VoiceProblem.ModelDownloadFailed, detail: ex.Message);
            return false;
        }
    }

    private void Rest(VoiceProblem problem = VoiceProblem.None, string? detail = null)
    {
        if (_disposed)
        {
            return;
        }

        if (_wakeWordEnabled)
        {
            try
            {
                _wakeWord.StartListening();
                SetState(VoiceState.WakeListening, problem, detail: detail);
                return;
            }
            catch (Exception ex)
            {
                _wakeWordEnabled = false;
                SetState(VoiceState.Ready, VoiceProblem.WakeWordUnavailable, detail: ex.Message);
                return;
            }
        }

        SetState(VoiceState.Ready, problem, detail: detail);
    }

    private void SetState(VoiceState state, VoiceProblem problem = VoiceProblem.None, double? progress = null, string? detail = null)
    {
        State = state;
        StateChanged?.Invoke(this, new VoiceStateChangedEventArgs(state, problem, progress, detail));
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
