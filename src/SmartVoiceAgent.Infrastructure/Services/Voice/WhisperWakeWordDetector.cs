using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using System.Threading.Channels;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Listens for the wake phrase on this computer: the recorder cuts speech into utterances, the small Whisper
/// model transcribes the start of each one, and <see cref="WakePhraseMatcher"/> looks for the phrase. Nothing
/// leaves the computer. Needs the <c>tiny</c> model in <see cref="ISpeechModelStore"/>.
/// </summary>
public sealed class WhisperWakeWordDetector : IWakeWordDetectionService
{
    private static readonly int CheckBytes = WaveAudio.SampleRate * 2 * 3;
    private static readonly int MaxUtteranceBytes = WaveAudio.SampleRate * 2 * 10;

    private readonly IVoiceRecognitionFactory _recorderFactory;
    private readonly WhisperSTTService _whisper;
    private readonly ISpeechModelStore _modelStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhisperWakeWordDetector> _logger;
    private readonly object _gate = new();
    private IVoiceRecognitionService? _recorder;
    private Channel<byte[]>? _utterances;
    private CancellationTokenSource? _stop;
    private string? _wakeWordOverride;
    private bool _disposed;

    /// <summary>
    /// Creates the detector.
    /// </summary>
    /// <param name="recorderFactory">Creates the microphone recorder.</param>
    /// <param name="whisper">Transcribes the utterances.</param>
    /// <param name="modelStore">Holds the small wake word model.</param>
    /// <param name="configuration">The configuration Settings write to.</param>
    /// <param name="logger">The logger.</param>
    public WhisperWakeWordDetector(
        IVoiceRecognitionFactory recorderFactory,
        WhisperSTTService whisper,
        ISpeechModelStore modelStore,
        IConfiguration configuration,
        ILogger<WhisperWakeWordDetector> logger)
    {
        _recorderFactory = recorderFactory;
        _whisper = whisper;
        _modelStore = modelStore;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<WakeWordDetectedEventArgs>? OnWakeWordDetected;

    /// <inheritdoc />
    public event EventHandler<Exception>? OnError;

    /// <inheritdoc />
    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _recorder is not null;
            }
        }
    }

    /// <inheritdoc />
    public string WakeWord => _wakeWordOverride ?? VoiceSettings.Read(_configuration).WakeWord;

    /// <inheritdoc />
    public float Sensitivity { get; set; } = 0.5f;

    /// <summary>
    /// Gets the similarity a transcript needs, from <see cref="Sensitivity"/>: 0.5 needs 0.7.
    /// </summary>
    public double Threshold => Math.Clamp(0.9 - 0.4 * Sensitivity, 0.5, 0.95);

    /// <inheritdoc />
    public void StartListening()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recorder is not null)
            {
                return;
            }

            if (!_modelStore.IsDownloaded(SpeechModelCatalog.WakeWordModel))
            {
                throw new InvalidOperationException("The wake word model isn't downloaded.");
            }

            _stop = new CancellationTokenSource();
            _utterances = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

            var recorder = _recorderFactory.Create();
            recorder.OnVoiceCaptured += OnUtterance;
            recorder.OnError += OnRecorderError;
            _recorder = recorder;
            _ = Task.Run(() => CheckUtterancesAsync(_utterances.Reader, _stop.Token));
            try
            {
                recorder.StartListening();
            }
            catch
            {
                StopCore();
                throw;
            }
        }

        _logger.LogInformation("Listening for the wake phrase '{WakeWord}'", WakeWord);
    }

    /// <inheritdoc />
    public void StopListening()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    /// <inheritdoc />
    public bool SetWakeWord(string wakeWord)
    {
        if (string.IsNullOrWhiteSpace(wakeWord) || WakePhraseMatcher.Key(wakeWord).Length < 3)
        {
            return false;
        }

        _wakeWordOverride = wakeWord.Trim();
        return true;
    }

    /// <summary>
    /// Transcribes the start of an utterance and returns the match, without raising events.
    /// </summary>
    /// <param name="utterance">16 kHz mono 16-bit PCM.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    public async Task<WakePhraseMatch> CheckAsync(byte[] utterance, CancellationToken cancellationToken = default)
    {
        var start = utterance.Length > CheckBytes ? utterance[..CheckBytes] : utterance;
        var settings = VoiceSettings.Read(_configuration);
        var result = await _whisper.TranscribeAsync(
                start,
                SpeechModelCatalog.WakeWordModel,
                settings.Language,
                prompt: WakeWord,
                cancellationToken)
            .ConfigureAwait(false);
        return WakePhraseMatcher.Match(result.Text, WakeWord, Threshold);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            StopCore();
            _disposed = true;
        }
    }

    private void StopCore()
    {
        var recorder = _recorder;
        _recorder = null;
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
        _utterances?.Writer.TryComplete();
        _utterances = null;
        if (recorder is not null)
        {
            recorder.OnVoiceCaptured -= OnUtterance;
            recorder.OnError -= OnRecorderError;
            recorder.Dispose();
        }
    }

    private void OnUtterance(object? sender, byte[] utterance)
    {
        if (utterance.Length > MaxUtteranceBytes)
        {
            // Long speech is conversation, not a call to Kam; only its start can hold the phrase.
            utterance = utterance[..MaxUtteranceBytes];
        }

        _utterances?.Writer.TryWrite(utterance);
    }

    private void OnRecorderError(object? sender, Exception error)
    {
        _logger.LogWarning(error, "Wake word microphone failed");
        OnError?.Invoke(this, error);
    }

    private async Task CheckUtterancesAsync(ChannelReader<byte[]> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var utterance in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var match = await CheckAsync(utterance, cancellationToken).ConfigureAwait(false);
                if (!match.IsMatch || cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                _logger.LogInformation("Wake phrase heard (similarity {Score:F2})", match.Score);
                OnWakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs(WakeWord, (float)match.Score)
                {
                    FollowingText = match.FollowingText,
                    Utterance = utterance
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wake word check failed");
            OnError?.Invoke(this, ex);
        }
    }
}
